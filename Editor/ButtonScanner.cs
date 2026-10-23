using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>How safely a style can replace a look.</summary>
public enum MatchConfidence
{
    /// <summary>Nothing close.</summary>
    None,
    /// <summary>Close, but the sprite, transition or colour differs enough that a person should check first.</summary>
    Check,
    /// <summary>Same sprite and transition, colours within tolerance: applying changes nothing a player would notice
    /// beyond normalising near-duplicate shades and corners. Safe to apply unattended.</summary>
    Safe,
}

/// <summary>One unstyled control, addressed so it can be found again after its scene or prefab is reloaded.</summary>
public class ButtonRef
{
    public string AssetPath;
    public bool InScene;
    /// <summary>Sibling indices from the prefab root, or from the scene's root list. Names repeat; indices don't.</summary>
    public string IndexPath;
    /// <summary>Set when the scene was already open during the scan, so the edit goes through Undo.</summary>
    public Component Live;
    /// <summary>The button's name at scan time. Applying skips a button whose name no longer matches, so a hierarchy
    /// edited between scan and apply can never receive a style meant for a different button.</summary>
    public string Name;
    public string Display;
    /// <summary>The <see cref="ControlSet"/> this control belongs to, or null.</summary>
    public string Set;
    public string SetName;
    public bool On;
}

/// <summary>
/// Controls that should share one style — a ToggleGroup's toggles — with the looks they have now. A set whose
/// members look different is held back from automatic styling and resolved as one in Style Review.
/// </summary>
public class ControlSet
{
    public string Id, Name;
    public readonly List<(ButtonRef Ref, LookGroup Look)> Members = new List<(ButtonRef, LookGroup)>();
    public UIComponentStyle Choice;

    public int Count => Members.Count;
    /// <summary>One group per distinct appearance, whatever the members' On state.</summary>
    public List<LookGroup> Looks => Distinct(Members.Select(m => m.Look));
    public bool Mixed => Looks.Count > 1;

    /// <summary>
    /// Whether the set can be styled unattended: every member matches the same style with no visible change.
    /// Otherwise the whole set waits for Style Review, so a group is never left half-styled.
    /// </summary>
    public bool Ready =>
        Members.All(m => m.Look.Confidence == MatchConfidence.Safe) && Members.Select(m => m.Look.Suggested).Distinct().Count() == 1;

    /// <summary>The style suggested for the most members, preferring safer matches.</summary>
    public UIComponentStyle Suggestion =>
        Members.Where(m => m.Look.Suggested != null).GroupBy(m => m.Look.Suggested)
               .OrderByDescending(g => g.Max(m => m.Look.Confidence)).ThenByDescending(g => g.Count())
               .Select(g => g.Key).FirstOrDefault();

    /// <summary>
    /// When every On member shares one look and every Off member another, those are the set's two states — the
    /// usual case of a selected tab authored in its selected colour.
    /// </summary>
    public bool TryGetStates(out LookGroup off, out LookGroup on)
    {
        List<LookGroup> offLooks = Distinct(Members.Where(m => !m.Ref.On).Select(m => m.Look));
        List<LookGroup> onLooks = Distinct(Members.Where(m => m.Ref.On).Select(m => m.Look));
        off = offLooks.Count == 1 ? offLooks[0] : null;
        on = onLooks.Count == 1 ? onLooks[0] : null;
        return off != null && on != null && off.LookKey != on.LookKey && off.Kind == on.Kind
               && off.HasBackground && !off.IsPicture && on.HasBackground && !on.IsPicture;
    }

    private static List<LookGroup> Distinct(IEnumerable<LookGroup> looks) => looks.GroupBy(l => l.LookKey).Select(g => g.First()).ToList();
}

/// <summary>Controls of one kind that currently look identical.</summary>
public class LookGroup
{
    public IComponentKind Kind = ComponentKinds.Buttons;
    public ScanTraits Traits = ScanTraits.None;
    /// <summary>Whether these controls are in their On state; matched against a style's On look.</summary>
    public bool On;
    /// <summary>Identifies the appearance alone, so On and Off controls that look the same compare equal.</summary>
    public string LookKey;
    public bool HasBackground;
    /// <summary>The visible image is a picture (an unsliced sprite: icon, character, checkbox) rather than a button
    /// shape. Never auto-styled or used to generate a style.</summary>
    public bool IsPicture;
    public Sprite Sprite;
    public Image.Type ImageType = Image.Type.Sliced;
    public float Multiplier = 1f;
    public float Radius;
    public Color Fill;
    public Color Visible;
    public bool HasLabel;
    public Color Label;
    public Selectable.Transition Transition = Selectable.Transition.ColorTint;
    /// <summary>The state colours, which carry meaning when a script relies on one — e.g. Selected marking a chosen answer.</summary>
    public ColorBlock Colors = ColorBlock.defaultColorBlock;
    public Vector2 SizeSum;
    public readonly List<ButtonRef> Buttons = new List<ButtonRef>();

    public UIComponentStyle Suggested;
    public MatchConfidence Confidence;
    public UIComponentStyle Choice;

    public int Count => Buttons.Count;
    /// <summary>E.g. "Button", "Toggle · Checkbox".</summary>
    public string KindName => (string.IsNullOrEmpty(Traits.Variety) ? Kind.DisplayName : $"{Kind.DisplayName} · {Traits.Variety}") + (On ? " (on)" : "");
    public Vector2 AverageSize => Count == 0 ? Vector2.zero : SizeSum / Count;
}

public class ScanResult
{
    public List<LookGroup> Groups = new List<LookGroup>();
    public int Styled, InPrefabInstances, Prefabs, Scenes, Disabled;
    /// <summary>Controls marked <see cref="UniqueDesign"/>: deliberately their own look, never proposed a style.</summary>
    public int Unique;
    private Dictionary<string, ControlSet> sets;

    public int Pictures => Groups.Where(g => g.IsPicture).Sum(g => g.Count);

    public IEnumerable<LookGroup> Where(MatchConfidence c) => Groups.Where(g => g.Confidence == c);
    public int CountWhere(MatchConfidence c) => Where(c).Sum(g => g.Count);

    public IReadOnlyCollection<ControlSet> Sets => SetsById.Values;

    /// <summary>Whether the control waits for its set to be styled as one; see <see cref="ControlSet.Ready"/>.</summary>
    public bool IsHeld(ButtonRef b) => b.Set != null && SetsById.TryGetValue(b.Set, out ControlSet set) && !set.Ready;

    /// <summary>The controls of these groups that can be styled on their own: every one not held for its set.</summary>
    public IEnumerable<(ButtonRef target, UIComponentStyle style)> Work(IEnumerable<LookGroup> groups, bool useChoice) =>
        groups.SelectMany(g => g.Buttons.Where(b => !IsHeld(b)).Select(b => (b, useChoice ? g.Choice : g.Suggested)));

    public int CountApplicable(IEnumerable<LookGroup> groups) => groups.Sum(g => g.Buttons.Count(b => !IsHeld(b)));

    private Dictionary<string, ControlSet> SetsById => sets ??= Groups
        .SelectMany(g => g.Buttons.Where(b => b.Set != null).Select(b => (b, g)))
        .GroupBy(m => m.b.Set)
        .ToDictionary(s => s.Key, s =>
        {
            var set = new ControlSet { Id = s.Key, Name = s.First().b.SetName };
            set.Members.AddRange(s);
            return set;
        });
}

/// <summary>
/// Finds unstyled controls of every scannable kind across prefabs and scenes, matches them to styles of their kind,
/// and applies styles in bulk.
/// </summary>
public static class ButtonScanner
{
    /// <summary>True while a scan or bulk apply is opening scenes and adding components.</summary>
    public static bool Busy { get; private set; }

    /// <summary>Layout problems found on the buttons the last <see cref="Apply"/> styled — reported, never auto-fixed.</summary>
    public static readonly List<string> LastLayoutWarnings = new List<string>();

    public static ScanResult Scan(DesignSystemSettings settings)
    {
        var result = new ScanResult();
        var byKey = new Dictionary<string, LookGroup>();
        Busy = true;
        try
        {
            if (settings.scanPrefabs) ScanPrefabs(settings, result, byKey);
            ScanScenes(settings, result, byKey);
        }
        finally
        {
            Busy = false;
            EditorUtility.ClearProgressBar();
        }
        result.Groups = byKey.Values.OrderByDescending(g => g.Count).ToList();
        return result;
    }

    private static void ScanPrefabs(DesignSystemSettings settings, ScanResult result, Dictionary<string, LookGroup> byKey)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (settings.IsExcluded(path)) continue;
            if (i % 25 == 0) EditorUtility.DisplayProgressBar("Scanning prefabs", path, (float)i / guids.Length);

            // Thousands of loaded prefabs exhaust editor memory on a large project; the scan keeps only paths
            // and sprites, so everything else can be released as it goes.
            if (result.Prefabs > 0 && result.Prefabs % 250 == 0) EditorUtility.UnloadUnusedAssetsImmediate();

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            result.Prefabs++;
            foreach (Component b in ComponentKinds.Scannable(asset))
            {
                // Buttons from a nested prefab or a variant's base belong to that source prefab.
                if (PrefabUtility.GetCorrespondingObjectFromSource(b) != null) continue;
                Add(byKey, result, b, new ButtonRef
                {
                    Name = b.name,
                    AssetPath = path,
                    IndexPath = IndexPath(asset.transform, b.transform),
                    Display = $"{path} › {HierarchyPath(b.transform)}",
                });
            }
        }
    }

    private static void ScanScenes(DesignSystemSettings settings, ScanResult result, Dictionary<string, LookGroup> byKey)
    {
        List<string> paths = settings.ScenePaths();
        for (int i = 0; i < paths.Count; i++)
        {
            EditorUtility.DisplayProgressBar("Scanning scenes", paths[i], (float)i / Math.Max(1, paths.Count));
            WithScene(paths[i], (scene, wasOpen) =>
            {
                result.Scenes++;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                    foreach (Component b in ComponentKinds.Scannable(roots[r]))
                    {
                        if (PrefabUtility.IsPartOfPrefabInstance(b)) { result.InPrefabInstances++; continue; }
                        string rest = IndexPath(roots[r].transform, b.transform);
                        Add(byKey, result, b, new ButtonRef
                        {
                            Name = b.name,
                            AssetPath = scene.path,
                            InScene = true,
                            IndexPath = rest.Length == 0 ? r.ToString() : $"{r}/{rest}",
                            Live = wasOpen ? b : null,
                            Display = $"{(string.IsNullOrEmpty(scene.path) ? scene.name : scene.path)} › {HierarchyPath(b.transform)}",
                        });
                    }
                return false;
            });
        }
    }

    /// <summary>
    /// Runs the action on a scene, opening it additively when it isn't already loaded. A scene opened here is
    /// saved when the action returns true and then closed; a scene the user had open is left open and unsaved.
    /// </summary>
    internal static void WithScene(string path, Func<Scene, bool, bool> action)
    {
        Scene scene = string.IsNullOrEmpty(path) ? default : SceneManager.GetSceneByPath(path);
        if (string.IsNullOrEmpty(path))
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isLoaded && string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    action(SceneManager.GetSceneAt(i), true);
            return;
        }

        bool wasOpen = scene.IsValid() && scene.isLoaded;
        if (!wasOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            bool save = action(scene, wasOpen);
            if (save && !wasOpen) EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// Scans only under the given objects in open scenes or the prefab stage. Controls owned by the scene are live, so
    /// applying them goes through Undo and saves nothing. A control inside a prefab instance belongs to the prefab
    /// that defines it: with includePrefabSources that prefab's control is scanned instead — once, however many
    /// instances show it — and styling it saves the prefab; otherwise it is only counted.
    /// </summary>
    public static ScanResult ScanUnder(IEnumerable<GameObject> roots, bool includePrefabSources = false, DesignSystemSettings settings = null)
    {
        var result = new ScanResult();
        var byKey = new Dictionary<string, LookGroup>();
        var sources = new HashSet<Component>();
        foreach (Component b in roots.Where(r => r != null).SelectMany(ComponentKinds.Scannable).Distinct())
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(b))
            {
                Add(byKey, result, b, new ButtonRef { Name = b.name, Live = b, Display = HierarchyPath(b.transform) });
                continue;
            }
            Component source = includePrefabSources ? PrefabUtility.GetCorrespondingObjectFromOriginalSource(b) : null;
            string path = source != null ? AssetDatabase.GetAssetPath(source) : null;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/") || (settings != null && settings.IsExcluded(path)))
            {
                result.InPrefabInstances++;
                continue;
            }
            if (!sources.Add(source)) continue;
            Add(byKey, result, source, new ButtonRef
            {
                Name = source.name,
                AssetPath = path,
                IndexPath = IndexPath(source.transform.root, source.transform),
                Display = $"{path} › {HierarchyPath(source.transform)}",
            });
        }
        result.Prefabs = sources.Select(AssetDatabase.GetAssetPath).Distinct().Count();
        result.Groups = byKey.Values.OrderByDescending(g => g.Count).ToList();
        return result;
    }

    private static void Add(Dictionary<string, LookGroup> byKey, ScanResult result, Component b, ButtonRef entry)
    {
        if (b.TryGetComponent(out StyledElement _)) { result.Styled++; return; }
        if (b.TryGetComponent(out UniqueDesign _)) { result.Unique++; return; }
        // A disabled control component means the object isn't acting as one — often a leftover on art.
        if (b is Behaviour behaviour && !behaviour.enabled) { result.Disabled++; return; }
        var control = b as Selectable;
        Selectable.Transition transition = control != null ? control.transition : Selectable.Transition.None;
        ColorBlock colors = control != null ? control.colors : ColorBlock.defaultColorBlock;

        IComponentKind kind = ComponentKinds.For(b);
        Component set = kind.SetOf(b);
        if (set != null)
        {
            entry.Set = $"{entry.AssetPath}#{set.GetInstanceID()}";
            entry.SetName = set.name;
        }
        entry.On = kind.IsOn(b);
        ScanTraits traits = kind.Read(b);
        Image bg = kind.BackgroundOf(b);
        TMP_Text label = kind.LabelOf(b);
        Color fill = bg != null ? bg.color : Color.clear;
        Color ink = label != null ? label.color : Color.clear;
        string lookKey = string.Join("|", kind.DisplayName, traits.Key, bg != null, bg != null && bg.sprite != null ? bg.sprite.GetInstanceID() : 0,
                                 bg != null ? bg.type : 0, bg != null ? bg.pixelsPerUnitMultiplier.ToString("0.##") : "",
                                 ColorUtility.ToHtmlStringRGBA(fill), label != null, ColorUtility.ToHtmlStringRGBA(ink), transition,
                                 transition == Selectable.Transition.ColorTint ? StateKey(colors) : "");

        string key = $"{lookKey}|{entry.On}";
        if (!byKey.TryGetValue(key, out LookGroup g))
        {
            byKey[key] = g = new LookGroup
            {
                Kind = kind,
                Traits = traits,
                On = entry.On,
                LookKey = lookKey,
                HasBackground = bg != null,
                IsPicture = bg != null && bg.sprite != null && !SurfaceLook.IsShape(bg.sprite),
                Sprite = bg != null ? bg.sprite : null,
                ImageType = bg != null ? bg.type : Image.Type.Simple,
                Multiplier = bg != null ? bg.pixelsPerUnitMultiplier : 1f,
                Radius = CornerRadius(bg),
                Fill = fill,
                Visible = fill * SpriteCentreColour(bg != null ? bg.sprite : null),
                HasLabel = label != null,
                Label = ink,
                Transition = transition,
                Colors = colors,
            };
        }
        g.Buttons.Add(entry);
        g.SizeSum += ((RectTransform)b.transform).rect.size;
    }

    /// <summary>The rendered corner radius in canvas pixels of a sliced Image, or 0.</summary>
    public static float CornerRadius(Image img)
    {
        if (img == null || img.sprite == null || img.type != Image.Type.Sliced || img.sprite.border.x <= 0f) return 0f;
        return img.sprite.border.x / (img.pixelsPerUnit * img.pixelsPerUnitMultiplier);
    }

    // ---- Matching ----

    /// <summary>
    /// How well a style fits a look. Colours are compared as they appear — fill × sprite under Tint, the fill
    /// itself under Recolor — because some sprites carry their colour baked in under a white fill. The style's
    /// corner is not compared: unifying corners is what styling is for. Only styles of the group's kind match, and
    /// controls that are on are compared with the style's On look.
    /// </summary>
    public static MatchConfidence Match(LookGroup g, UIComponentStyle style, float tolerance, out float rank)
    {
        rank = float.MaxValue;
        if (!CanTake(g, style)) return MatchConfidence.None;

        SurfaceLook look = style.LookFor(g.On);
        Sprite styleSprite = look.ResolveSprite(style.Theme);
        bool sameSprite = styleSprite == null || styleSprite == g.Sprite;
        Sprite rendered = styleSprite != null ? styleSprite : g.Sprite;
        bool recolors = look.fillMode == SurfaceFillMode.Recolor && look.shape.IsSet;
        Color visible = !look.fill.TryResolve(style.Theme, out Color fill) ? g.Visible
            : recolors ? fill
            : fill * SpriteCentreColour(rendered);
        float fillDistance = ColorDistance(visible, g.Visible);
        float labelDistance = g.HasLabel && style.LabelColorFor(g.On).TryResolve(style.Theme, out Color ink) ? ColorDistance(ink, g.Label) : 0f;
        bool statesKept = KeepsStates(style, g, tolerance);

        rank = fillDistance + labelDistance * 0.5f + (sameSprite ? 0f : 0.2f) + (statesKept ? 0f : 0.2f);
        if (sameSprite && statesKept && fillDistance <= tolerance && labelDistance <= tolerance
            && g.Kind.Keeps(style, g.Traits, tolerance)) return MatchConfidence.Safe;
        return fillDistance <= 0.35f ? MatchConfidence.Check : MatchConfidence.None;
    }

    /// <summary>Whether the style could be put on these controls at all: a shape (not a picture) of a kind and build it suits.</summary>
    public static bool CanTake(LookGroup g, UIComponentStyle style) =>
        style != null && g.HasBackground && !g.IsPicture && g.Kind.StyleType.IsInstanceOfType(style) && g.Kind.Suits(style, g.Traits);

    /// <summary>
    /// Whether applying the style keeps how the controls react to hover, press and selection: a style that sets state
    /// tints only fits controls already tinting with those colours — so a None or Sprite Swap transition, or a custom
    /// Selected colour that a script relies on, is never replaced unattended.
    /// </summary>
    public static bool KeepsStates(UIComponentStyle style, LookGroup g, float tolerance)
    {
        if (!style.ApplyStateTints) return true;
        if (g.Transition != Selectable.Transition.ColorTint) return false;
        return SameStates(style.StateColors, g.Colors, tolerance);
    }

    public static bool SameStates(ColorBlock a, ColorBlock b, float tolerance) =>
        ColorDistance(a.highlightedColor, b.highlightedColor) <= tolerance && ColorDistance(a.pressedColor, b.pressedColor) <= tolerance
        && ColorDistance(a.selectedColor, b.selectedColor) <= tolerance && ColorDistance(a.disabledColor, b.disabledColor) <= tolerance;

    private static string StateKey(ColorBlock c) => string.Join(",", ColorUtility.ToHtmlStringRGBA(c.highlightedColor),
        ColorUtility.ToHtmlStringRGBA(c.pressedColor), ColorUtility.ToHtmlStringRGBA(c.selectedColor), ColorUtility.ToHtmlStringRGBA(c.disabledColor));

    /// <summary>Picks each group's best style; Safe beats Check, then the lower rank wins. Only a Safe pick is
    /// pre-chosen — a Check is a suggestion a person confirms, since applying it changes something on screen.</summary>
    public static void Suggest(IEnumerable<LookGroup> groups, IEnumerable<UIComponentStyle> styles, float tolerance)
    {
        List<UIComponentStyle> candidates = styles.ToList();
        foreach (LookGroup g in groups)
        {
            g.Suggested = null;
            g.Confidence = MatchConfidence.None;
            float bestRank = float.MaxValue;
            foreach (UIComponentStyle style in candidates)
            {
                MatchConfidence c = Match(g, style, tolerance, out float rank);
                if (c > g.Confidence || (c == g.Confidence && c != MatchConfidence.None && rank < bestRank))
                {
                    g.Confidence = c;
                    g.Suggested = style;
                    bestRank = rank;
                }
            }
            g.Choice = g.Confidence == MatchConfidence.Safe ? g.Suggested : null;
        }
    }

    public static float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b, da = a.a - b.a;
        return Mathf.Sqrt(dr * dr + dg * dg + db * db + da * da);
    }

    private static readonly Dictionary<Sprite, Color> centreCache = new Dictionary<Sprite, Color>();

    /// <summary>The colour at the sprite's centre — white for none. Sampled through a 1×1 GPU blit because
    /// textures are rarely CPU-readable.</summary>
    public static Color SpriteCentreColour(Sprite sprite)
    {
        if (sprite == null) return Color.white;
        if (centreCache.TryGetValue(sprite, out Color cached)) return cached;

        Texture2D tex = sprite.texture;
        Rect r = sprite.rect;
        var scale = new Vector2(r.width * 0.1f / tex.width, r.height * 0.1f / tex.height);
        var offset = new Vector2((r.x + r.width * 0.45f) / tex.width, (r.y + r.height * 0.45f) / tex.height);

        RenderTexture rt = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(tex, rt, scale, offset);
        RenderTexture.active = rt;
        var read = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        Color colour = read.GetPixel(0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        UnityEngine.Object.DestroyImmediate(read);

        centreCache[sprite] = colour;
        return colour;
    }

    // ---- Applying ----

    /// <summary>
    /// Styles every listed control. Buttons in scenes that were open during the scan go through Undo; closed
    /// scenes are opened, saved and closed; prefabs are edited as assets. Returns how many buttons were styled.
    /// </summary>
    public static int Apply(IEnumerable<(ButtonRef target, UIComponentStyle style)> work)
    {
        var items = work.Where(w => w.style != null).ToList();
        int styled = 0;
        LastLayoutWarnings.Clear();
        Busy = true;
        try
        {
            Undo.SetCurrentGroupName("Apply styles");
            int undoGroup = Undo.GetCurrentGroup();
            foreach (var (target, style) in items.Where(w => w.target.Live != null))
                if (!target.Live.TryGetComponent(out StyledElement _))
                {
                    DesignSystemAssets.Attach(target.Live, style);
                    NoteLayoutIssues(target.Live.transform, style, target.Display);
                    styled++;
                }
            Undo.CollapseUndoOperations(undoGroup);

            var byAsset = items.Where(w => w.target.Live == null).GroupBy(w => (w.target.AssetPath, w.target.InScene)).ToList();
            for (int i = 0; i < byAsset.Count; i++)
            {
                var asset = byAsset[i];
                EditorUtility.DisplayProgressBar("Applying styles", asset.Key.AssetPath, (float)i / byAsset.Count);
                if (asset.Key.InScene)
                    WithScene(asset.Key.AssetPath, (scene, _) =>
                    {
                        int before = styled;
                        GameObject[] roots = scene.GetRootGameObjects();
                        foreach (var (target, style) in asset)
                            if (AttachAt(FindInScene(roots, target.IndexPath), target, style)) styled++;
                        return styled > before;
                    });
                else
                    styled += ApplyToPrefab(asset.Key.AssetPath, asset);
            }
        }
        finally
        {
            Busy = false;
            EditorUtility.ClearProgressBar();
        }
        return styled;
    }

    private static int ApplyToPrefab(string path, IEnumerable<(ButtonRef target, UIComponentStyle style)> items)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int styled = items.Count(w => AttachAt(FindByIndexPath(root.transform, w.target.IndexPath), w.target, w.style));
            if (styled > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            return styled;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool AttachAt(Transform t, ButtonRef target, UIComponentStyle style)
    {
        if (t == null || (target.Name != null && t.name != target.Name) || t.TryGetComponent(out StyledElement _)) return false;
        Component control = t.GetComponents<Component>().FirstOrDefault(c => ComponentKinds.Fits(style, c));
        if (control == null) return false;
        DesignSystemAssets.Attach(control, style, recordUndo: false);
        NoteLayoutIssues(t, style, target.Display);
        return true;
    }

    private static void NoteLayoutIssues(Transform t, UIComponentStyle style, string where)
    {
        foreach (LayoutIssue issue in LayoutDiagnostics.Diagnose((RectTransform)t, style))
            LastLayoutWarnings.Add($"{where}: {issue.Message}");
    }

    private static Transform FindInScene(GameObject[] roots, string indexPath)
    {
        int slash = indexPath.IndexOf('/');
        int r = int.Parse(slash < 0 ? indexPath : indexPath.Substring(0, slash));
        if (r >= roots.Length) return null;
        return FindByIndexPath(roots[r].transform, slash < 0 ? "" : indexPath.Substring(slash + 1));
    }

    public static string IndexPath(Transform root, Transform t)
    {
        var parts = new List<string>();
        for (; t != null && t != root; t = t.parent) parts.Insert(0, t.GetSiblingIndex().ToString());
        return string.Join("/", parts);
    }

    public static Transform FindByIndexPath(Transform root, string path)
    {
        Transform t = root;
        if (string.IsNullOrEmpty(path)) return t;
        foreach (string part in path.Split('/'))
        {
            int i = int.Parse(part);
            if (i >= t.childCount) return null;
            t = t.GetChild(i);
        }
        return t;
    }

    public static string HierarchyPath(Transform t)
    {
        string path = t.name;
        for (t = t.parent; t != null; t = t.parent) path = t.name + "/" + path;
        return path;
    }
}
