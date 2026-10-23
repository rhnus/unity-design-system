using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// ---- The snapshot Tools/figma_sync.py writes ----

public class FigmaSnapshot
{
    public string fileKey, fileName, figmaVersion, figmaLastModified, syncedAt;
    public List<FigmaComponent> components = new List<FigmaComponent>();

    public FigmaComponent Find(string id) => components.FirstOrDefault(c => c.id == id);
}

public class FigmaComponent
{
    public string id, name, description;
    public List<FigmaVariant> variants = new List<FigmaVariant>();

    public FigmaVariant Variant(string name) =>
        string.IsNullOrEmpty(name) ? null : variants.FirstOrDefault(v => v.name == name);

    /// <summary>The variant that shows the component at rest: State=Default, Enabled or Normal, else the first.</summary>
    public FigmaVariant RestingVariant() =>
        variants.FirstOrDefault(v => v.props != null && v.props.TryGetValue("State", out string s) && RestingStates.Contains(s))
        ?? variants.FirstOrDefault();

    public FigmaVariant StateVariant(string state) =>
        variants.FirstOrDefault(v => v.props != null && v.props.TryGetValue("State", out string s) && s == state);

    private static readonly string[] RestingStates = { "Default", "Enabled", "Normal", "Rest" };
}

public class FigmaVariant
{
    public string id, name;
    public Dictionary<string, string> props = new Dictionary<string, string>();
    public float? width, height, cornerRadius, strokeWeight;
    public string fill, stroke;
    public FigmaLabel label;
}

public class FigmaLabel
{
    public string sample, fontFamily, colour;
    public float? fontWeight, fontSize, lineHeight;
}

// ---- Files the team edits ----

/// <summary>Which Figma component a Unity style follows, and which of its variants are the resting, hover and disabled looks.</summary>
public class FigmaMapEntry
{
    public string figmaId, figmaName, unityStyle, restingVariant, hoverVariant, disabledVariant;
    /// <summary>False while the entry is only the sync's suggestion; a person confirms it once.</summary>
    public bool confirmed;
    public string note = "";
}

/// <summary>A value the game changed on purpose: the sync keeps it until the line is removed.</summary>
public class FigmaAdaptation
{
    public string style, field, unityValue, figmaValue, reason, decidedBy, date;
}

/// <summary>What the three-way compare decided for one field.</summary>
public enum FigmaResult
{
    InSync,
    /// <summary>First sync, or the component is new to the baseline: a person marks it adapted or "follow Figma".</summary>
    ToTriage,
    /// <summary>Marked as adapted for the game, and Figma hasn't moved since.</summary>
    Adapted,
    /// <summary>Unity differs from a Figma value that hasn't changed, and nobody has marked it yet.</summary>
    UnityChanged,
    /// <summary>Figma changed and Unity still has the old Figma value: propose the change.</summary>
    DesignUpdate,
    /// <summary>Marked adapted, but Figma has moved since: reported, the mark still wins.</summary>
    AdaptedFigmaMoved,
    /// <summary>Figma changed and Unity differs from both the old and the new Figma value.</summary>
    Conflict,
}

public class FigmaFieldRow
{
    public string style, figmaComponent, field, figmaNow, figmaBaseline, unityNow;
    public bool mappingConfirmed;
    public FigmaResult result;
    public FigmaAdaptation adaptation;
}

public class FigmaReport
{
    public FigmaSnapshot snapshot;
    public bool hasBaseline;
    public List<FigmaFieldRow> rows = new List<FigmaFieldRow>();
    /// <summary>Figma components no map entry covers, with the style the sync suggests (or none).</summary>
    public List<FigmaMapEntry> unmapped = new List<FigmaMapEntry>();
    /// <summary>Components in the baseline that are gone from Figma; nothing is removed in Unity.</summary>
    public List<FigmaComponent> removed = new List<FigmaComponent>();
    /// <summary>Map entries naming a Unity style that no longer exists.</summary>
    public List<FigmaMapEntry> missingStyles = new List<FigmaMapEntry>();

    public IEnumerable<FigmaFieldRow> RowsFor(string style) => rows.Where(r => r.style == style);
}

/// <summary>
/// Compares a Figma snapshot with the Design System, three ways: Figma now, Figma at the last accepted sync (the
/// baseline) and Unity now. It changes nothing in Unity. Only a Figma change on a value Unity still has from the
/// baseline is a design update to propose; anything the game changed is kept, or raised for people to decide.
/// The rules are in docs/figma-unity-pipeline.md.
/// </summary>
public static class FigmaSync
{
    public static string Folder => DesignSystemDoc.Folder + "/figma";
    public static string SnapshotPath => $"{Folder}/components.json";
    public static string BaselinePath => $"{Folder}/baseline/components.json";
    public static string MapPath => $"{Folder}/component-map.json";
    public static string AdaptationsPath => $"{Folder}/adaptations.json";

    public static readonly string[] Fields = { "fill", "corner", "height", "labelColour", "labelSize", "labelFont" };

    // ---- Running ----

    /// <summary>
    /// Reads the snapshot, baseline, map and adaptations; adds suggestions for unmapped components to the map file;
    /// compares; and rewrites DESIGN_SYSTEM.md with the results. Returns the report.
    /// </summary>
    public static FigmaReport Run()
    {
        FigmaSnapshot now = Load<FigmaSnapshot>(SnapshotPath)
                            ?? throw new FileNotFoundException($"No Figma snapshot yet. Run: python Tools/figma_sync.py fetch", SnapshotPath);
        FigmaSnapshot baseline = Load<FigmaSnapshot>(BaselinePath);
        List<FigmaMapEntry> map = Load<List<FigmaMapEntry>>(MapPath) ?? new List<FigmaMapEntry>();
        List<FigmaAdaptation> adaptations = Load<List<FigmaAdaptation>>(AdaptationsPath) ?? new List<FigmaAdaptation>();
        UITheme theme = DesignSystemAssets.FindSingle<UITheme>();
        List<UIComponentStyle> styles = DesignSystemAssets.FindAll<UIComponentStyle>();

        FigmaReport report = Compare(now, baseline, map, adaptations, styles, theme);
        foreach (FigmaMapEntry suggestion in report.unmapped.Where(u => !string.IsNullOrEmpty(u.unityStyle)))
            map.Add(suggestion);
        Save(MapPath, map.OrderBy(m => m.figmaName, StringComparer.Ordinal).ToList());
        if (!File.Exists(AdaptationsPath)) Save(AdaptationsPath, adaptations);
        DesignSystemDoc.Write(report: report);
        return report;
    }

    /// <summary>Makes the current snapshot the baseline, once the sync has been reviewed and the triage done.</summary>
    public static void AcceptBaseline()
    {
        if (!File.Exists(SnapshotPath)) throw new FileNotFoundException("No Figma snapshot to accept.", SnapshotPath);
        Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath));
        File.Copy(SnapshotPath, BaselinePath, overwrite: true);
    }

    /// <summary>Records that a style's field differs from Figma on purpose, with the values as they are now.</summary>
    public static void MarkAdapted(string style, string field, string reason, string decidedBy)
    {
        FigmaReport report = Run();
        FigmaFieldRow row = report.rows.FirstOrDefault(r => r.style == style && r.field == field)
                            ?? throw new ArgumentException($"No compared field '{field}' on '{style}'. Is the style mapped?");
        List<FigmaAdaptation> list = Load<List<FigmaAdaptation>>(AdaptationsPath) ?? new List<FigmaAdaptation>();
        list.RemoveAll(a => a.style == style && a.field == field);
        list.Add(new FigmaAdaptation
        {
            style = style, field = field, unityValue = row.unityNow, figmaValue = row.figmaNow, reason = reason,
            decidedBy = decidedBy, date = DateTime.Now.ToString("yyyy-MM-dd"),
        });
        Save(AdaptationsPath, list.OrderBy(a => a.style, StringComparer.Ordinal).ThenBy(a => a.field, StringComparer.Ordinal).ToList());
    }

    /// <summary>Removes an adaptation mark, so the next sync treats the field as following Figma.</summary>
    public static bool Unmark(string style, string field)
    {
        List<FigmaAdaptation> list = Load<List<FigmaAdaptation>>(AdaptationsPath) ?? new List<FigmaAdaptation>();
        bool removed = list.RemoveAll(a => a.style == style && a.field == field) > 0;
        if (removed) Save(AdaptationsPath, list);
        return removed;
    }

    [MenuItem(DesignSystemMenu.Root + "Figma/Compare With Figma Snapshot", priority = 30)]
    private static void RunFromMenu()
    {
        FigmaReport r = Run();
        Debug.Log($"[DesignSystem] Compared {r.rows.Select(x => x.style).Distinct().Count()} mapped style(s) with Figma " +
                  $"{r.snapshot.fileName} v{r.snapshot.figmaVersion}: {Summary(r.rows)}. {r.unmapped.Count} unmapped component(s). " +
                  $"See {DesignSystemDoc.Folder}/{DesignSystemDoc.FileName}.");
    }

    [MenuItem(DesignSystemMenu.Root + "Figma/Accept Snapshot As Baseline", priority = 31)]
    private static void AcceptFromMenu()
    {
        if (!EditorUtility.DisplayDialog("Accept Figma snapshot", "Make the current Figma snapshot the baseline? Do this after the " +
                "sync has been reviewed and every 'to triage' row is marked adapted or settled.", "Accept", "Cancel")) return;
        AcceptBaseline();
        Debug.Log($"[DesignSystem] {SnapshotPath} is now the baseline.");
    }

    public static string Summary(IEnumerable<FigmaFieldRow> rows) =>
        string.Join(", ", rows.GroupBy(r => r.result).OrderBy(g => g.Key).Select(g => $"{g.Count()} {Label(g.Key)}"));

    public static string Label(FigmaResult result) => result switch
    {
        FigmaResult.InSync => "in sync",
        FigmaResult.ToTriage => "to triage",
        FigmaResult.Adapted => "adapted for the game",
        FigmaResult.UnityChanged => "changed in Unity, not marked",
        FigmaResult.DesignUpdate => "design update to review",
        FigmaResult.AdaptedFigmaMoved => "adapted; Figma moved since",
        _ => "conflict",
    };

    // ---- Comparing ----

    public static FigmaReport Compare(FigmaSnapshot now, FigmaSnapshot baseline, IList<FigmaMapEntry> map,
                                      IList<FigmaAdaptation> adaptations, IList<UIComponentStyle> styles, UITheme theme)
    {
        var report = new FigmaReport { snapshot = now, hasBaseline = baseline != null };
        foreach (FigmaMapEntry entry in map.Where(m => !string.IsNullOrEmpty(m.unityStyle)))
        {
            FigmaComponent component = now.Find(entry.figmaId);
            if (component == null) continue;
            UIComponentStyle style = styles.FirstOrDefault(s => s != null && s.name == entry.unityStyle);
            if (style == null) { report.missingStyles.Add(entry); continue; }

            FigmaVariant current = component.Variant(entry.restingVariant) ?? component.RestingVariant();
            FigmaComponent oldComponent = baseline?.Find(entry.figmaId);
            FigmaVariant old = oldComponent == null ? null : oldComponent.Variant(current?.name) ?? oldComponent.RestingVariant();
            Dictionary<string, string> figma = FigmaValues(current), figmaOld = old == null ? null : FigmaValues(old), unity = UnityValues(style, theme);

            foreach (string field in Fields)
            {
                figma.TryGetValue(field, out string f);
                if (f == null) continue;
                unity.TryGetValue(field, out string u);
                string b = null;
                figmaOld?.TryGetValue(field, out b);
                FigmaAdaptation mark = adaptations.FirstOrDefault(a => a.style == style.name && a.field == field);
                report.rows.Add(new FigmaFieldRow
                {
                    style = style.name, figmaComponent = $"{component.name}{(current != null && current.name != "" ? $" · {current.name}" : "")}",
                    field = field, figmaNow = f, figmaBaseline = b, unityNow = u, mappingConfirmed = entry.confirmed,
                    adaptation = mark, result = Decide(f, b, u, oldComponent != null, mark != null),
                });
            }
        }

        var mapped = new HashSet<string>(map.Select(m => m.figmaId));
        foreach (FigmaComponent c in now.components.Where(c => !mapped.Contains(c.id)))
            report.unmapped.Add(Suggest(c, styles, theme));
        if (baseline != null)
            report.removed.AddRange(baseline.components.Where(c => now.Find(c.id) == null));
        return report;
    }

    /// <summary>The three-way rule for one value; see <see cref="FigmaResult"/>.</summary>
    public static FigmaResult Decide(string figmaNow, string figmaBaseline, string unityNow, bool hasBaseline, bool marked)
    {
        bool unityMatchesNow = Same(unityNow, figmaNow);
        if (!hasBaseline) return unityMatchesNow ? FigmaResult.InSync : marked ? FigmaResult.Adapted : FigmaResult.ToTriage;

        bool figmaChanged = !Same(figmaNow, figmaBaseline);
        if (!figmaChanged)
            return unityMatchesNow ? FigmaResult.InSync : marked ? FigmaResult.Adapted : FigmaResult.UnityChanged;
        if (marked) return FigmaResult.AdaptedFigmaMoved;
        if (unityMatchesNow) return FigmaResult.InSync;
        return Same(unityNow, figmaBaseline) ? FigmaResult.DesignUpdate : FigmaResult.Conflict;
    }

    /// <summary>Values compare as text: colours as #RRGGBB(AA), numbers to the nearest half pixel, fonts as "Family Weight".</summary>
    public static bool Same(string a, string b)
    {
        if (a == null || b == null) return a == b;
        if (float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            return Mathf.Abs(x - y) <= 0.5f;
        return string.Equals(NormaliseColour(a), NormaliseColour(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormaliseColour(string s) =>
        s.StartsWith("#") && s.Length == 9 && s.EndsWith("FF", StringComparison.OrdinalIgnoreCase) ? s.Substring(0, 7) : s;

    public static Dictionary<string, string> FigmaValues(FigmaVariant v)
    {
        var values = new Dictionary<string, string>();
        if (v == null) return values;
        if (v.fill != null) values["fill"] = v.fill;
        if (v.cornerRadius != null) values["corner"] = Number(v.cornerRadius.Value);
        if (v.height != null) values["height"] = Number(v.height.Value);
        if (v.label?.colour != null) values["labelColour"] = v.label.colour;
        if (v.label?.fontSize != null) values["labelSize"] = Number(v.label.fontSize.Value);
        if (v.label?.fontFamily != null) values["labelFont"] = FontName(v.label.fontFamily, v.label.fontWeight ?? 400f);
        return values;
    }

    /// <summary>What the style gives its controls, in the same terms; a field the style leaves to each control is absent.</summary>
    public static Dictionary<string, string> UnityValues(UIComponentStyle style, UITheme theme)
    {
        var values = new Dictionary<string, string>();
        if (style.Background.fill.TryResolve(theme ?? style.Theme, out Color fill)) values["fill"] = Hex(fill);
        if (style.Background.cornerRadius > 0f) values["corner"] = Number(style.Background.cornerRadius);
        if (style.Height > 0f) values["height"] = Number(style.Height);
        if (style.LabelColor.TryResolve(theme ?? style.Theme, out Color ink)) values["labelColour"] = Hex(ink);

        TypographyScale scale = (theme ?? style.Theme)?.Typography;
        TypographyScale.RoleStyle role = style.TryGetLabelRoleId(out string r) && scale != null ? scale.Get(r) : null;
        if (style.LabelSize > 0f) values["labelSize"] = Number(style.LabelSize);
        else if (role != null && !role.autoSize) values["labelSize"] = Number(role.fontSize);
        if (role?.font != null) values["labelFont"] = FontName(role.font.name, role.fontStyle);
        return values;
    }

    /// <summary>
    /// Every fill, stroke and label colour in the snapshot that no theme token has, with the components using it,
    /// most used first: a palette Figma uses that the game doesn't have names for.
    /// </summary>
    public static List<(string colour, List<string> components)> ColoursOutsideTheme(FigmaSnapshot snapshot, UITheme theme)
    {
        var known = new HashSet<string>((theme?.Colors ?? new List<UITheme.ColorToken>()).Where(t => t != null)
                                        .Select(t => NormaliseColour(Hex(t.value))), StringComparer.OrdinalIgnoreCase);
        var uses = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (FigmaComponent c in snapshot.components)
            foreach (FigmaVariant v in c.variants)
                foreach (string colour in new[] { v.fill, v.stroke, v.label?.colour }.Where(x => x != null))
                {
                    string key = NormaliseColour(colour).ToUpperInvariant();
                    if (known.Contains(key)) continue;
                    if (!uses.TryGetValue(key, out SortedSet<string> names)) uses[key] = names = new SortedSet<string>(StringComparer.Ordinal);
                    names.Add(c.name);
                }
        return uses.OrderByDescending(u => u.Value.Count).ThenBy(u => u.Key, StringComparer.Ordinal)
                   .Select(u => (u.Key, u.Value.ToList())).ToList();
    }

    // ---- Suggesting a map ----

    /// <summary>
    /// The Unity style that draws a Figma component most closely, judged on its resting variant. The fill must match;
    /// label colour, label size, height and corner then rank the candidates. A style named for a use case (_Send) or
    /// a square icon style only wins for a component that shares that, so a matching height never outranks the role.
    /// A match on the fill alone, or on a plain white fill and one other value, says too little and suggests nothing.
    /// The note lists what matched, for the person who confirms it.
    /// </summary>
    public static FigmaMapEntry Suggest(FigmaComponent component, IEnumerable<UIComponentStyle> styles, UITheme theme)
    {
        FigmaVariant resting = component.RestingVariant();
        var entry = new FigmaMapEntry
        {
            figmaId = component.id, figmaName = component.name, restingVariant = resting?.name,
            hoverVariant = component.StateVariant("Hover")?.name, disabledVariant = component.StateVariant("Disabled")?.name,
        };
        if (resting == null) return entry;
        Dictionary<string, string> figma = FigmaValues(resting);
        if (!figma.TryGetValue("fill", out string fill)) { entry.note = "No solid fill to match on."; return entry; }
        bool square = resting.width != null && resting.height != null && Mathf.Abs(resting.width.Value - resting.height.Value) <= 2f;

        UIComponentStyle best = null;
        float bestScore = float.MaxValue;
        foreach (UIComponentStyle s in styles.Where(s => s != null && !(s is UISurfaceStyle)))
        {
            Dictionary<string, string> unity = UnityValues(s, theme);
            if (!unity.TryGetValue("fill", out string u) || !Same(u, fill)) continue;
            float score = Penalty(figma, unity, "labelColour", 1f) + Penalty(figma, unity, "labelSize", 1f)
                          + Distance(figma, unity, "height", 56f) + Distance(figma, unity, "corner", 20f)
                          + (HasUseCase(s.name, theme) ? 1f : 0f) + (s.Square != square ? 1f : 0f) + (s.IsVariant ? 0.01f : 0f);
            if (score < bestScore) { bestScore = score; best = s; }
        }
        if (best == null) { entry.note = $"No Unity style has the fill {fill}."; return entry; }

        List<string> matched = Matched(figma, UnityValues(best, theme));
        if (matched.Count == 1 || (Same(fill, "#FFFFFF") && matched.Count < 3))
        {
            string what = matched.Count == 1 ? "the fill matches" : $"{string.Join(" and ", matched)} match";
            entry.note = $"Only {what} {best.name}: too little to suggest. Map it by hand if it should follow a style.";
            return entry;
        }
        entry.unityStyle = best.name;
        entry.note = $"Suggested: matches {string.Join(", ", matched)}. Check it, then set confirmed to true.";
        return entry;
    }

    private static bool HasUseCase(string styleName, UITheme theme) => DesignSystemDoc.ExpectedFigmaName(styleName, theme).Contains("(use case");

    private static List<string> Matched(Dictionary<string, string> figma, Dictionary<string, string> unity)
    {
        var matched = new List<string> { "fill" };
        bool Close(string field, float within) =>
            figma.TryGetValue(field, out string a) && unity.TryGetValue(field, out string b)
            && float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out float y) && Mathf.Abs(x - y) <= within;
        if (figma.TryGetValue("labelColour", out string fc) && unity.TryGetValue("labelColour", out string uc) && Same(fc, uc)) matched.Add("label colour");
        if (Close("labelSize", 0.5f)) matched.Add("label size");
        if (Close("height", 4f)) matched.Add("height");
        if (Close("corner", 2f)) matched.Add("corner");
        return matched;
    }

    private static float Penalty(Dictionary<string, string> a, Dictionary<string, string> b, string field, float weight) =>
        a.TryGetValue(field, out string x) && b.TryGetValue(field, out string y) && !Same(x, y) ? weight : 0f;

    private static float Distance(Dictionary<string, string> a, Dictionary<string, string> b, string field, float scale) =>
        a.TryGetValue(field, out string x) && b.TryGetValue(field, out string y)
        && float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float fx)
        && float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out float fy)
            ? Mathf.Abs(fx - fy) / scale
            : 0.5f;

    // ---- Shared formatting ----

    private static readonly (int weight, string word)[] Weights =
    {
        (100, "Thin"), (200, "ExtraLight"), (300, "Light"), (400, "Regular"), (500, "Medium"),
        (600, "SemiBold"), (700, "Bold"), (800, "ExtraBold"), (900, "Black"),
    };

    /// <summary>"Outfit SemiBold" from Figma's family and numeric weight.</summary>
    public static string FontName(string family, float weight)
    {
        string word = Weights.OrderBy(w => Mathf.Abs(w.weight - weight)).First().word;
        return $"{family} {word}";
    }

    /// <summary>"Outfit SemiBold" from a TMP font asset name such as "Outfit-SemiBold SDF"; a bold style raises the weight to Bold.</summary>
    public static string FontName(string fontAssetName, TMPro.FontStyles style)
    {
        (string family, int weight) = ParseFontAsset(fontAssetName);
        if ((style & TMPro.FontStyles.Bold) != 0 && weight < 700) weight = 700;
        return FontName(family, weight);
    }

    /// <summary>Family and weight from a TMP font asset name: "Outfit-SemiBold SDF" is Outfit, 600; an unknown suffix is 400.</summary>
    public static (string family, int weight) ParseFontAsset(string name)
    {
        string bare = name.Replace(" SDF", "").Trim();
        int dash = bare.IndexOf('-');
        string family = dash > 0 ? bare.Substring(0, dash) : bare;
        string suffix = dash > 0 ? bare.Substring(dash + 1).Replace(" ", "") : "";
        (int weight, string word) match = Weights.FirstOrDefault(w => string.Equals(w.word, suffix, StringComparison.OrdinalIgnoreCase));
        return (family, match.word != null ? match.weight : 400);
    }

    public static string Hex(Color c)
    {
        string rgba = ColorUtility.ToHtmlStringRGBA(c);
        return "#" + (rgba.EndsWith("FF") ? rgba.Substring(0, 6) : rgba);
    }

    public static string Number(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    // ---- Files ----

    public static T Load<T>(string path) where T : class =>
        File.Exists(path) ? JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) : null;

    public static void Save<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented,
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }) + "\n");
    }
}
