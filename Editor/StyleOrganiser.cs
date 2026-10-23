using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>What organising does to one style.</summary>
public enum OrganiseAction
{
    /// <summary>Stays as it is; renamed only.</summary>
    Keep,
    /// <summary>Becomes a variant of its role's style, overriding only its size, icon shape and what it must keep.</summary>
    Variant,
    /// <summary>Its buttons move to an identical style and it is removed.</summary>
    Merge,
    /// <summary>No button uses it; it is removed.</summary>
    Delete,
}

/// <summary>One style's place in the organised set, as proposed and then edited by the designer.</summary>
public class OrganiseRow
{
    public UIComponentStyle Style;
    public StyleUsage Usage;
    public int Uses;
    public bool IsRoot;
    public string Role;
    public UIComponentStyle Parent;
    public OrganiseAction Action;
    /// <summary>The named size the style is put at, or null to keep its own height.</summary>
    public UITheme.SizeToken Size;
    /// <summary>The nearest named size, offered for a one-off height.</summary>
    public UITheme.SizeToken NearestSize;
    public bool Icon;
    public string UseCase = "";
    public string NewName = "";
    public bool Selected = true;
    /// <summary>What the style's buttons will look like differently afterwards, in words. Empty = nothing visible changes.</summary>
    public List<string> Changes = new List<string>();
    /// <summary>Something the designer has to decide; the row isn't applied until it's gone or deselected.</summary>
    public string Warning;
    /// <summary>Other rows heading for this row's new name.</summary>
    public List<OrganiseRow> Clashes = new List<OrganiseRow>();
    /// <summary>Where the old name says something the style doesn't do. Informational; never blocks the row.</summary>
    public string Note;
    /// <summary>The named size the old name claims, when the style isn't at it.</summary>
    public UITheme.SizeToken NameSaysSize;
    /// <summary>Whether the old name claims an icon-only button the style doesn't make square.</summary>
    public bool NameSaysIcon;
}

/// <summary>
/// Organises one kind's styles the way the README explains them: a <b>role</b> is a look (Primary, Secondary, Ghost —
/// fill, shape, label colour); a role comes in the theme's <b>named sizes</b> (Regular, Small, XSmall); and either
/// size can be an <b>icon</b> (square) button. Width isn't a style matter — layouts and the buttons themselves own it.
/// So every style is either a role, a variant of one at a size and shape, an exact duplicate to merge, or unused.
/// Names are built from that place — Button_Role[_Size][_Icon][_UseCase] — so they can't drift from the settings.
/// </summary>
public static class StyleOrganiser
{
    public static List<OrganiseRow> Plan(IEnumerable<StyleUsage> usages, UITheme theme, IComponentKind kind)
    {
        List<OrganiseRow> rows = usages.Where(u => u.Style != null && kind.StyleType.IsInstanceOfType(u.Style))
            .Select(u => new OrganiseRow { Style = u.Style, Usage = u, Uses = u.References, Icon = u.Style.Square })
            .ToList();

        foreach (OrganiseRow row in rows) row.Role = RoleOf(row.Style.name, kind);
        foreach (OrganiseRow row in rows.Where(r => r.Role == Custom))
        {
            string look = LookKey(row.Style);
            OrganiseRow named = rows.Where(r => r.Role != Custom && LookKey(r.Style) == look).OrderByDescending(r => r.Uses).FirstOrDefault();
            if (named != null) row.Role = named.Role;
        }
        foreach (IGrouping<string, OrganiseRow> role in rows.GroupBy(r => r.Role))
        {
            OrganiseRow root = role.OrderByDescending(r => r.Uses).ThenBy(r => r.Style.name.Length).First();
            root.IsRoot = true;
            foreach (OrganiseRow row in role)
            {
                row.Parent = root.Style;
                row.Action = row.IsRoot ? OrganiseAction.Keep : OrganiseAction.Variant;
            }
        }

        foreach (OrganiseRow row in rows) PlaceSize(row, theme);
        PlanMembers(rows);
        foreach (OrganiseRow row in rows.Where(r => r.Style.Locked)) { row.Action = OrganiseAction.Keep; row.Selected = false; }
        foreach (OrganiseRow row in rows)
        {
            row.UseCase = UseCaseOf(row.Style.name, row.Role, kind);
            NoteWhatTheNameClaims(row, theme);
        }
        Refresh(rows, theme, kind);
        NameClashesApart(rows, theme, kind);
        Refresh(rows, theme, kind);
        // Applying the plan as proposed never changes how a button looks; a row that would starts unticked.
        foreach (OrganiseRow row in rows.Where(r => r.Changes.Count > 0)) row.Selected = false;
        return rows;
    }

    // ---- Where each style goes ----

    private static void PlaceSize(OrganiseRow row, UITheme theme)
    {
        float h = row.Style.Height;
        row.Size = null;
        row.NearestSize = null;
        if (h <= 0f || theme == null) return;
        UITheme.SizeToken exact = theme.Sizes.FirstOrDefault(s => Mathf.Approximately(s.height, h));
        if (exact != null) { row.Size = exact; return; }
        row.NearestSize = theme.Sizes.OrderBy(s => Mathf.Abs(s.height - h)).FirstOrDefault();
    }

    // A style that draws exactly what another of its role at its size and shape draws, give or take width, is a
    // duplicate: width belongs to layouts. The most-used one (the role itself, when it's one of them) stays.
    private static void PlanMembers(List<OrganiseRow> rows)
    {
        foreach (IGrouping<string, OrganiseRow> same in rows.GroupBy(r => $"{r.Role}|{SizeKey(r)}|{r.Icon}"))
        {
            var kept = new List<OrganiseRow>();
            foreach (OrganiseRow row in same.OrderByDescending(r => r.IsRoot).ThenByDescending(r => r.Uses).ThenBy(r => r.Style.name.Length))
            {
                OrganiseRow into = row.IsRoot ? null : kept.FirstOrDefault(k => OnlyWidthDiffers(row.Style, k.Style));
                if (into == null) { kept.Add(row); continue; }
                row.Action = Unused(row) ? OrganiseAction.Delete : OrganiseAction.Merge;
                row.Parent = into.Style;
            }
        }
        foreach (OrganiseRow row in rows.Where(r => r.Action == OrganiseAction.Variant && Unused(r)))
        {
            row.Action = OrganiseAction.Delete;
            row.Selected = false;
        }

        // An icon at a smaller size follows the text style of that size, so the tree reads Primary ▸ Small ▸ Small Icon.
        foreach (OrganiseRow icon in rows.Where(r => r.Action == OrganiseAction.Variant && r.Icon))
        {
            OrganiseRow sameSize = rows.FirstOrDefault(r => r != icon && r.Role == icon.Role && !r.Icon && !r.IsRoot
                && r.Action == OrganiseAction.Variant && SizeKey(r) == SizeKey(icon));
            if (sameSize != null) icon.Parent = sameSize.Style;
        }
    }

    private const string Custom = "Custom";

    private static bool OnlyWidthDiffers(UIComponentStyle a, UIComponentStyle b) =>
        DifferenceNames(a, b).All(d => d == "width" || d == "min width");

    private static readonly Regex Pixels = new Regex(@"^(\d+)px$", RegexOptions.IgnoreCase);

    // Old names were written by hand and some no longer describe the style ("Small" on a 56px button).
    private static void NoteWhatTheNameClaims(OrganiseRow row, UITheme theme)
    {
        string[] words = Regex.Split(row.Style.name, @"[^A-Za-z0-9]+").Where(w => w.Length > 0).ToArray();
        float h = row.Style.Height;
        var claims = new List<string>();
        var facts = new List<string>();

        UITheme.SizeToken said = theme == null ? null
            : theme.Sizes.FirstOrDefault(z => words.Any(w => w.Equals(z.name, System.StringComparison.OrdinalIgnoreCase)));
        if (said != null && h > 0f && !Mathf.Approximately(said.height, h))
        {
            row.NameSaysSize = said;
            claims.Add($"{said.name} ({said.height:0.#}px)");
            facts.Add($"{h:0.#}px tall");
        }
        foreach (string w in words)
        {
            Match px = Pixels.Match(w);
            if (px.Success && h > 0f && float.Parse(px.Groups[1].Value, CultureInfo.InvariantCulture) is float n
                && !Mathf.Approximately(n, h) && !Mathf.Approximately(n, row.Style.Width))
            {
                claims.Add(w);
                if (!facts.Contains($"{h:0.#}px tall")) facts.Add($"{h:0.#}px tall");
            }
        }
        if (words.Any(w => w.Equals("IconOnly", System.StringComparison.OrdinalIgnoreCase)) && !row.Style.Square)
        {
            row.NameSaysIcon = true;
            claims.Add("icon-only");
            facts.Add("not square");
        }
        if (claims.Count > 0)
            row.Note = $"Its old name says {string.Join(" and ", claims)}, but it's {string.Join(" and ", facts)}. The new name says what it really is.";
    }

    private static bool Unused(OrganiseRow row) => row.Uses == 0 && row.Usage.OtherReferences.Count == 0;

    /// <summary>Recomputes every row's name, warnings and previewed changes after the designer edits one.</summary>
    public static void Refresh(List<OrganiseRow> rows, UITheme theme, IComponentKind kind)
    {
        foreach (OrganiseRow row in rows)
        {
            row.Warning = null;
            row.Clashes.Clear();
            row.NewName = row.Action == OrganiseAction.Merge || row.Action == OrganiseAction.Delete ? "" : NameFor(row, kind);
            row.Changes = Preview(row, rows);
            if (row.Size == null && row.NearestSize != null && row.Action == OrganiseAction.Variant && string.IsNullOrEmpty(row.UseCase))
                row.Warning = $"One-off height {row.Style.Height:0.#}px: snap to {row.NearestSize.name}, or name the special case with a use-case word.";
        }

        foreach (IGrouping<string, OrganiseRow> clash in rows.Where(r => r.NewName.Length > 0).GroupBy(r => r.NewName).Where(g => g.Count() > 1))
            foreach (OrganiseRow row in clash.Where(r => !r.IsRoot || clash.Count(c => c.IsRoot) > 1))
            {
                row.Clashes.AddRange(clash.Where(c => c != row));
                row.Warning = $"Would be named {row.NewName}, the same as " +
                              string.Join(" and ", row.Clashes.Select(c => $"{c.Style.name}, which differs only in {Listed(DifferenceNames(row.Style, c.Style))}")) +
                              ". Merge into it if they should be one style, or give this one a use-case word.";
            }

        foreach (OrganiseRow row in rows.Where(r => r.Action == OrganiseAction.Merge))
        {
            OrganiseRow into = rows.FirstOrDefault(r => r.Style == row.Parent);
            if (into != null && (into.Action == OrganiseAction.Merge || into.Action == OrganiseAction.Delete))
                row.Warning = $"{into.Style.name} is itself going away. Merge into {(into.Action == OrganiseAction.Merge ? into.Parent.name : "another style")} instead.";
        }
    }

    // Styles heading for one name usually still say how they differ in their old names (TextOnly, TextWithIcon);
    // those words become their use cases. A word the style doesn't live up to — IconOnly on a style that isn't
    // square — is never borrowed, so such a row keeps its warning for the designer.
    private static void NameClashesApart(List<OrganiseRow> rows, UITheme theme, IComponentKind kind)
    {
        foreach (OrganiseRow row in rows.Where(r => r.Clashes.Count > 0 && string.IsNullOrEmpty(r.UseCase)))
        {
            var others = new HashSet<string>(row.Clashes.SelectMany(c => NameWords(c.Style.name)), System.StringComparer.OrdinalIgnoreCase);
            row.UseCase = string.Concat(NameWords(row.Style.name)
                .Where(w => !others.Contains(w) && Telling(w, row, theme, kind))
                .Select(Word));
        }
    }

    private static string[] NameWords(string name) => Regex.Split(name, @"[^A-Za-z0-9]+").Where(w => w.Length > 0).ToArray();

    private static bool Telling(string word, OrganiseRow row, UITheme theme, IComponentKind kind)
    {
        if (word.Equals(kind.DisplayName, System.StringComparison.OrdinalIgnoreCase) || word.Equals(row.Role, System.StringComparison.OrdinalIgnoreCase)) return false;
        if (Regex.IsMatch(word, @"^(\d+(px)?|h\d+|w\d+|style|buttonstyle|icon)$", RegexOptions.IgnoreCase)) return false;
        if (theme != null && theme.Sizes.Any(z => word.Equals(z.name, System.StringComparison.OrdinalIgnoreCase))) return false;
        return !(word.Equals("IconOnly", System.StringComparison.OrdinalIgnoreCase) && !row.Style.Square);
    }

    private static string Listed(List<string> names) => names.Count == 0 ? "settings that don't show" : string.Join(", ", names);

    // ---- Names ----

    /// <summary>Kind_Role[_Size][_Icon][_UseCase]: the size only when it isn't the role's own, the use case only when given.</summary>
    public static string NameFor(OrganiseRow row, IComponentKind kind)
    {
        var parts = new List<string> { kind.DisplayName, row.Role };
        if (row.Size != null && !IsRegular(row.Size)) parts.Add(Word(row.Size.name));
        else if (row.Size == null && row.Style.Height > 0f && row.NearestSize != null) parts.Add($"H{row.Style.Height:0.#}");
        if (row.Icon) parts.Add("Icon");
        if (!string.IsNullOrEmpty(row.UseCase)) parts.Add(Word(row.UseCase));
        return string.Join("_", parts);
    }

    private static bool IsRegular(UITheme.SizeToken size) => size.name == "Regular";

    // The words an old name carries beyond what the new name says on its own — a use case such as "Send".
    private static readonly Regex Noise = new Regex(
        @"^(button|buttons|buttonstyle|style|small|xsmall|regular|large|medium|icon|icons|icononly|textonly|textwithicon|text|withicon|" +
        @"compact|compactwidth|width\d*(px)?|minwidth\d*(px)?|w\d+|min\d+|h\d+|\d+(px)?)$", RegexOptions.IgnoreCase);

    public static string UseCaseOf(string oldName, string role, IComponentKind kind)
    {
        IEnumerable<string> words = Regex.Split(oldName, @"[^A-Za-z0-9]+").Where(w => w.Length > 0);
        words = words.Where(w => !Noise.IsMatch(w) && !w.Equals(role, System.StringComparison.OrdinalIgnoreCase)
                                 && !w.Equals(kind.DisplayName, System.StringComparison.OrdinalIgnoreCase));
        return string.Concat(words.Select(Word));
    }

    /// <summary>The role a style's name declares — the word after the kind, e.g. "Primary" in Button_Primary_Small.</summary>
    public static string RoleOf(string name, IComponentKind kind)
    {
        string[] words = Regex.Split(name, @"[^A-Za-z0-9]+").Where(w => w.Length > 0).ToArray();
        if (words.Length > 1 && words[0].Equals(kind.DisplayName, System.StringComparison.OrdinalIgnoreCase)) return Word(words[1]);
        return Custom;
    }

    private static string Word(string text) =>
        string.Concat(Regex.Split(text, @"[^A-Za-z0-9]+").Where(w => w.Length > 0)
                           .Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w.Substring(1)));

    // ---- Keys ----

    private static string SizeKey(OrganiseRow row) => row.Size != null ? row.Size.id : $"h{row.Style.Height:0.#}";

    /// <summary>What makes a role: what the button looks like, not how big it is.</summary>
    public static string LookKey(UIComponentStyle style)
    {
        UITheme theme = style.Theme;
        Sprite sprite = style.Background.ResolveSprite(theme);
        return $"{Hex(style.Background.fill, theme)}|{(sprite != null ? sprite.GetInstanceID() : 0)}|{style.Background.fillMode}|{Hex(style.LabelColor, theme)}";
    }

    private static string Hex(ThemeColor colour, UITheme theme) => colour.TryResolve(theme, out Color c) ? ColorUtility.ToHtmlStringRGBA(c) : "-";

    // ---- What changes on screen ----

    /// <summary>
    /// What the row's buttons will show differently, worked out by applying the row to a throwaway copy of its
    /// style — the same code Apply runs — and comparing what each copy draws.
    /// </summary>
    private static List<string> Preview(OrganiseRow row, List<OrganiseRow> rows)
    {
        if (row.Action == OrganiseAction.Delete || row.Action == OrganiseAction.Keep) return new List<string>();
        if (row.Action == OrganiseAction.Merge) return Differences(row.Style, row.Parent);

        UIComponentStyle parent = row.Parent;
        UIComponentStyle copy = Object.Instantiate(row.Style);
        try
        {
            MakeVariant(copy, parent, row.Size, row.Icon, save: false);
            return Differences(row.Style, copy);
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }
    }

    /// <summary>What differs between the two styles, as "what before → after" lines.</summary>
    public static List<string> Differences(UIComponentStyle before, UIComponentStyle after) =>
        Diff(before, after).Select(d => $"{d.what} {d.before} → {d.after}").ToList();

    /// <summary>Just the names of what differs, e.g. "text", "width".</summary>
    public static List<string> DifferenceNames(UIComponentStyle a, UIComponentStyle b) => Diff(a, b).Select(d => d.what).ToList();

    private static List<(string what, string before, string after)> Diff(UIComponentStyle before, UIComponentStyle after)
    {
        var changes = new List<(string, string, string)>();
        void Compare(string what, string a, string b) { if (a != b) changes.Add((what, a, b)); }
        UITheme ta = before.Theme, tb = after.Theme;
        Sprite sa = before.Background.ResolveSprite(ta), sb = after.Background.ResolveSprite(tb);
        Compare("shape", sa != null ? sa.name : "none", sb != null ? sb.name : "none");
        Compare("fill mode", before.Background.fillMode.ToString(), after.Background.fillMode.ToString());
        Compare("fill", Hex(before.Background.fill, ta), Hex(after.Background.fill, tb));
        Compare("label colour", Hex(before.LabelColor, ta), Hex(after.LabelColor, tb));
        Compare("corner", $"{before.Background.cornerRadius:0.#}", $"{after.Background.cornerRadius:0.#}");
        Compare("height", Px(before.Height), Px(after.Height));
        Compare("icon", before.Square ? "square" : "not square", after.Square ? "square" : "not square");
        Compare("width", Px(before.Width), Px(after.Width));
        Compare("min width", Px(before.MinWidth), Px(after.MinWidth));
        Compare("text", TextLook(before), TextLook(after));
        Compare("side padding", Px(before.LabelPadding), Px(after.LabelPadding));
        Compare("hover tints", Tints(before), Tints(after));
        return changes;
    }

    // What the label draws, without the note on where the setting comes from.
    private static string TextLook(UIComponentStyle s) => Regex.Replace(DesignSystemDescribe.LabelFont(s), @"\s*\((theme|this style)\)", "");

    private static string Px(float px) => px > 0f ? $"{px:0.#}px" : "own";
    private static string Tints(UIComponentStyle s) => s.ApplyStateTints ? ColorUtility.ToHtmlStringRGB(s.StateColors.pressedColor) : "off";

    // ---- Applying ----

    private static readonly string[] SizeFields = { "heightSource", "height", "size" };

    /// <summary>
    /// Makes the style a variant of the parent at the given size (null = its own height) and shape. Every setting
    /// where it differs from the parent becomes an override, so linking it changes nothing on screen; only a size
    /// or icon shape the designer picked does.
    /// </summary>
    public static void MakeVariant(UIComponentStyle style, UIComponentStyle parent, UITheme.SizeToken size, bool icon, bool save = true)
    {
        if (save) Undo.RecordObject(style, "Organise style");
        List<string> own = DifferingFields(style, parent);

        style.BaseStyle = parent;
        style.Overrides.Clear();
        style.Overrides.AddRange(own.Where(f => !SizeFields.Contains(Top(f)) && f != "square"));
        bool sizeDiffers = size != null ? parent.NamedSize != size : own.Any(f => SizeFields.Contains(Top(f)));
        if (sizeDiffers) style.Overrides.AddRange(SizeFields);
        if (icon != parent.Square) style.Overrides.Add("square");
        VariantSync.Sync(style);

        if (size != null && sizeDiffers)
        {
            style.HeightSource = ButtonHeightSource.NamedSize;
            style.Size = ThemeSize.From(size);
        }
        style.Square = icon;
        if (!save) return;
        EditorUtility.SetDirty(style);
        AssetDatabase.SaveAssetIfDirty(style);
    }

    /// <summary>
    /// Gives the ticked rows without a warning their new names and changes nothing else. A name another of them is
    /// giving up is taken once it's free, so a swap of names lands on the exact names rather than "Name 1".
    /// </summary>
    public static int RenameOnly(List<OrganiseRow> rows, IComponentKind kind)
    {
        int renamed = RenameAll(rows.Where(r => r.Selected && r.Warning == null).ToList());
        AssetDatabase.SaveAssets();
        DesignChangeLog.Note($"renamed {renamed} {kind.DisplayName.ToLowerInvariant()} style(s)");
        return renamed;
    }

    private static int RenameAll(List<OrganiseRow> rows)
    {
        List<OrganiseRow> pending = rows.Where(r => r.Style != null && r.NewName.Length > 0 && r.Style.name != r.NewName).ToList();
        int renamed = pending.Count;
        while (pending.Count > 0)
        {
            OrganiseRow free = pending.FirstOrDefault(r => !NameTaken(r.Style, r.NewName));
            if (free == null)
            {
                // Every name left is held by another pending row: park one to break the cycle.
                StyleRenamer.Rename(pending[0].Style, pending[0].Style.name + "_Renaming");
                continue;
            }
            StyleRenamer.Rename(free.Style, free.NewName);
            pending.Remove(free);
        }
        return renamed;
    }

    private static bool NameTaken(UIComponentStyle style, string name)
    {
        string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(style)).Replace('\\', '/');
        return AssetDatabase.LoadMainAssetAtPath($"{folder}/{name}.asset") != null;
    }

    private static string Top(string path) => path.Split('.')[0];

    /// <summary>
    /// The settings whose saved values differ between the two styles. A differing group such as the background is
    /// split into its parts, so a variant with its own corner still follows its parent's fill.
    /// </summary>
    public static List<string> DifferingFields(UIComponentStyle style, UIComponentStyle other)
    {
        var fields = new List<string>();
        var theirs = new SerializedObject(other);
        SerializedProperty p = new SerializedObject(style).GetIterator();
        for (bool enter = true; p.NextVisible(enter); enter = false)
        {
            if (p.propertyPath == "m_Script" || VariantSync.IsBookkeeping(p.propertyPath)) continue;
            Collect(p, theirs, fields, depth: 0);
        }
        return fields;
    }

    private static void Collect(SerializedProperty p, SerializedObject theirs, List<string> fields, int depth)
    {
        SerializedProperty q = theirs.FindProperty(p.propertyPath);
        if (q != null && SerializedProperty.DataEquals(p, q)) return;
        if (q == null || depth > 0 || p.propertyType != SerializedPropertyType.Generic || p.isArray || !p.hasVisibleChildren)
        {
            fields.Add(p.propertyPath);
            return;
        }
        SerializedProperty child = p.Copy(), end = p.GetEndProperty();
        for (bool enter = true; child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end); enter = false)
            Collect(child, theirs, fields, depth + 1);
    }

    /// <summary>
    /// Carries out the selected rows: unused styles go, duplicates merge into the kept one (moving their buttons),
    /// variants are made — parents before children — and every remaining style takes its new name. Rows with a
    /// warning are skipped. Returns how many styles were changed.
    /// </summary>
    public static int Apply(List<OrganiseRow> rows, IComponentKind kind)
    {
        List<OrganiseRow> chosen = rows.Where(r => r.Selected && r.Warning == null).ToList();
        int changed = 0;

        List<StyleUsage> unused = chosen.Where(r => r.Action == OrganiseAction.Delete).Select(r => r.Usage).ToList();
        if (unused.Count > 0) { StyleConsolidation.Delete(unused); changed += unused.Count; }

        foreach (OrganiseRow row in chosen.Where(r => r.Action == OrganiseAction.Merge))
        {
            StyleUsage into = rows.FirstOrDefault(r => r.Style == row.Parent)?.Usage;
            if (into == null) continue;
            StyleConsolidation.Merge(new List<StyleUsage> { row.Usage }, into);
            changed++;
        }

        var made = new HashSet<UIComponentStyle>(rows.Where(r => r.Action != OrganiseAction.Variant).Select(r => r.Style));
        List<OrganiseRow> variants = chosen.Where(r => r.Action == OrganiseAction.Variant).ToList();
        while (variants.Count > 0)
        {
            OrganiseRow next = variants.FirstOrDefault(r => made.Contains(r.Parent) || variants.All(v => v.Style != r.Parent)) ?? variants[0];
            MakeVariant(next.Style, next.Parent, next.Size, next.Icon);
            made.Add(next.Style);
            variants.Remove(next);
            changed++;
        }

        changed += RenameAll(chosen);
        AssetDatabase.SaveAssets();
        DesignSystemAssets.ReapplyOpen();
        DesignChangeLog.Note($"organised {changed} {kind.DisplayName.ToLowerInvariant()} style(s)");
        return changed;
    }
}
