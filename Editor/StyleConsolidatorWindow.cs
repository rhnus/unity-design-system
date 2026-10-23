using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tidy Up Styles. <b>Merge</b> shows the button styles grouped by colour family with how many buttons use each,
/// and merges a whole family into one kept style in a click. <b>Organise</b> arranges what's left into roles, named
/// sizes and icon variants, and names each style after its place (see <see cref="StyleOrganiser"/>).
/// </summary>
public class StyleConsolidatorWindow : EditorWindow
{
    [MenuItem(DesignSystemMenu.Root + "Tidy Up Styles", priority = 2)]
    public static void Open()
    {
        var window = GetWindow<StyleConsolidatorWindow>("Tidy Up Styles");
        window.minSize = new Vector2(420f, 360f);
        window.Measure();
    }

    private List<StyleUsage> usages = new List<StyleUsage>();
    private List<StyleConsolidation.MergeGroup> groups = new List<StyleConsolidation.MergeGroup>();
    private readonly HashSet<StyleUsage> excluded = new HashSet<StyleUsage>();
    private readonly Dictionary<StyleUsage, UIComponentStyle> manualTargets = new Dictionary<StyleUsage, UIComponentStyle>();
    private List<OrganiseRow> plan = new List<OrganiseRow>();
    private UITheme theme;
    private int tab;
    private Vector2 scroll;
    private GUIStyle wrap;

    private static readonly string[] Tabs = { "Merge", "Organise" };

    private void Measure()
    {
        usages = StyleConsolidation.MeasureUsage();
        groups = StyleConsolidation.SuggestGroups(usages);
        excluded.Clear();
        manualTargets.Clear();
        theme = ThemeColorDrawer.ProjectTheme;
        DesignSystemAssets.EnsureThemeSizes(theme);
        plan = StyleOrganiser.Plan(usages, theme, ComponentKinds.Buttons);
        Repaint();
    }

    private void OnGUI()
    {
        wrap ??= new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(new GUIContent($"<b>{usages.Count}</b> button styles",
                "Merge: the buttons take the kept style's look. Remove: the buttons keep their current look and stop " +
                "following any style. Both edit prefabs and scenes directly — commit first."), wrap);
            if (GUILayout.Button("Refresh", GUILayout.Width(80))) Measure();
        }

        tab = GUILayout.Toolbar(tab, Tabs);
        EditorGUILayout.Space(4);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (tab == 1) DrawOrganise(); else DrawMerge();
        EditorGUILayout.EndScrollView();
        if (tab == 1) DrawOrganiseFooter();
        if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
    }

    private void DrawMerge()
    {
        List<StyleUsage> unused = usages.Where(u => u.References == 0 && u.OtherReferences.Count == 0).ToList();
        if (unused.Count > 0)
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Not used by any button ({unused.Count})", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(string.Join(", ", unused.Select(u => u.Style.name)), EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button($"Delete {unused.Count} unused style(s)") &&
                    EditorUtility.DisplayDialog("Delete unused styles", $"Delete {unused.Count} style(s) no button uses?", "Delete", "Cancel"))
                {
                    StyleConsolidation.Delete(unused);
                    Measure();
                    GUIUtility.ExitGUI();
                }
            }

        if (groups.Count > 0) EditorGUILayout.LabelField("Suggested merges", EditorStyles.boldLabel);
        foreach (StyleConsolidation.MergeGroup g in groups) DrawGroup(g);

        var grouped = new HashSet<StyleUsage>(groups.SelectMany(g => g.Members));
        List<StyleUsage> rest = usages.Where(u => u.References > 0 && !grouped.Contains(u)).ToList();
        if (rest.Count > 0)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(new GUIContent("The only style of its colour",
                "Fine to keep. Fold one into another style by picking it on the right, or Remove a legacy one."), EditorStyles.boldLabel);
            foreach (StyleUsage u in rest) DrawManualRow(u);
        }
    }

    // ---- Organise ----

    private void DrawOrganise()
    {
        EditorGUILayout.LabelField(
            "Each style is placed under its <b>role</b> (Primary, Secondary…) at one of the theme's <b>sizes</b>, and named " +
            "after that place: <i>Button_Role_Size_Icon_UseCase</i>. Width is left to layouts. Tick the rows to apply.", wrap);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            Legend("Link to X", "The style stays, and so do its buttons. It follows X except where it's different. Nothing on screen changes.");
            Legend("Merge into X", "The style is deleted. Its buttons switch to X and look exactly like X.");
            Legend("Keep apart", "The style stays as it is, unlinked. Only its name changes.");
            Legend("Delete", "No button uses it.");
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            string sizes = theme == null ? "no theme" : string.Join(" · ", theme.Sizes.Select(z => $"{z.name} {z.height:0.#}"));
            EditorGUILayout.LabelField($"Sizes: {sizes}", EditorStyles.miniLabel);
            if (theme != null && GUILayout.Button("Edit sizes", EditorStyles.miniButton, GUILayout.Width(70))) Selection.activeObject = theme;
        }
        EditorGUILayout.Space(4);

        foreach (OrganiseRow root in plan.Where(r => r.IsRoot).OrderByDescending(r => r.Uses))
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"<b>{root.Role}</b> role", wrap);
                DrawOrganiseRow(root, 0);
                foreach (OrganiseRow child in Children(root.Style)) DrawBranch(child, 1);
            }
    }

    private void Legend(string action, string meaning)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"<b>{action}</b>", wrap, GUILayout.Width(95));
            EditorGUILayout.LabelField(meaning, EditorStyles.wordWrappedMiniLabel);
        }
    }

    private IEnumerable<OrganiseRow> Children(UIComponentStyle parent) =>
        plan.Where(r => !r.IsRoot && r.Parent == parent).OrderBy(r => r.Action).ThenByDescending(r => r.Uses);

    private void DrawBranch(OrganiseRow row, int depth)
    {
        DrawOrganiseRow(row, depth);
        if (row.Action == OrganiseAction.Variant)
            foreach (OrganiseRow child in Children(row.Style)) DrawBranch(child, depth + 1);
    }

    private void DrawOrganiseRow(OrganiseRow row, int depth)
    {
        bool changed = false;
        bool shaping = row.Action == OrganiseAction.Variant || row.Action == OrganiseAction.Keep;
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(depth * 16f);
            using (new EditorGUI.DisabledScope(row.Style.Locked))
                row.Selected = EditorGUILayout.Toggle(row.Selected, GUILayout.Width(16));
            Swatch(row.Usage, 18);
            if (GUILayout.Button(new GUIContent(row.Style.name, "Select the style"), EditorStyles.label, GUILayout.MinWidth(120))) Select(row.Usage);
            EditorGUILayout.LabelField(new GUIContent($"{row.Uses}", "Buttons using it"), EditorStyles.miniLabel, GUILayout.Width(28));

            using (new EditorGUI.DisabledScope(row.Style.Locked))
                changed |= ActionPopup(row);

            using (new EditorGUI.DisabledScope(!shaping || row.IsRoot))
            {
                changed |= SizePopup(row);
                EditorGUI.BeginChangeCheck();
                row.Icon = GUILayout.Toggle(row.Icon, new GUIContent("Icon", "Square: width follows height."), EditorStyles.miniButton, GUILayout.Width(40));
                changed |= EditorGUI.EndChangeCheck();
            }
            using (new EditorGUI.DisabledScope(!shaping))
            {
                EditorGUI.BeginChangeCheck();
                GUI.SetNextControlName(UseCaseControl(row));
                row.UseCase = EditorGUILayout.DelayedTextField(new GUIContent("", "Use-case word, e.g. Send. Only for a style that really is special."),
                    row.UseCase, GUILayout.Width(90));
                changed |= EditorGUI.EndChangeCheck();
            }
        }

        string rename = row.NewName == row.Style.name ? "name already fits" : $"→ renamed {row.NewName}";
        string outcome = row.Action switch
        {
            OrganiseAction.Delete => "→ deleted",
            OrganiseAction.Merge => $"→ deleted; its {row.Uses} button(s) switch to {row.Parent.name}",
            _ => rename,
        };
        Indented(depth, () => EditorGUILayout.LabelField(outcome, EditorStyles.miniLabel));
        if (row.Changes.Count > 0)
            Indented(depth, () => EditorGUILayout.LabelField("Looks different: " + string.Join(", ", row.Changes), EditorStyles.wordWrappedMiniLabel));
        if (row.Warning != null) Indented(depth, () => DrawWarning(row));
        if (row.Note != null && shaping && !row.IsRoot) Indented(depth, () => DrawNote(row));

        if (changed) StyleOrganiser.Refresh(plan, theme, ComponentKinds.Buttons);
    }

    private bool ActionPopup(OrganiseRow row)
    {
        if (row.IsRoot)
        {
            EditorGUILayout.LabelField(new GUIContent("Role", "The style every other style in this box is placed under."), EditorStyles.miniBoldLabel, GUILayout.Width(150));
            return false;
        }
        var actions = new List<OrganiseAction> { OrganiseAction.Variant, OrganiseAction.Merge, OrganiseAction.Keep };
        if (row.Uses == 0 && row.Usage.OtherReferences.Count == 0) actions.Add(OrganiseAction.Delete);
        if (!actions.Contains(row.Action)) actions.Add(row.Action);
        string[] labels = actions.Select(a => a switch
        {
            OrganiseAction.Variant => $"Link to {row.Parent.name}",
            OrganiseAction.Merge => $"Merge into {row.Parent.name}",
            OrganiseAction.Keep => "Keep apart",
            _ => "Delete",
        }).ToArray();
        int current = actions.IndexOf(row.Action);
        EditorGUI.BeginChangeCheck();
        int picked = EditorGUILayout.Popup(current, labels, GUILayout.Width(150));
        if (!EditorGUI.EndChangeCheck()) return false;
        row.Action = actions[picked];
        return true;
    }

    private void DrawWarning(OrganiseRow row)
    {
        EditorGUILayout.HelpBox(row.Warning, MessageType.Warning);
        if (row.Clashes.Count == 0) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            foreach (OrganiseRow other in row.Clashes.Where(c => c.Action != OrganiseAction.Merge && c.Action != OrganiseAction.Delete))
                if (GUILayout.Button(new GUIContent($"Merge into {other.Style.name}", "This style is deleted and its buttons switch to that one."), EditorStyles.miniButton))
                {
                    row.Action = OrganiseAction.Merge;
                    row.Parent = other.Style;
                    Replan();
                }
            if (GUILayout.Button(new GUIContent("Name it…", "Type a use-case word in this row's last box."), EditorStyles.miniButton))
                EditorGUI.FocusTextInControl(UseCaseControl(row));
            GUILayout.FlexibleSpace();
        }
    }

    private void DrawNote(OrganiseRow row)
    {
        EditorGUILayout.HelpBox(row.Note, MessageType.Info);
        bool sizeFix = row.NameSaysSize != null && row.Size != row.NameSaysSize;
        bool iconFix = row.NameSaysIcon && !row.Icon;
        if (!sizeFix && !iconFix) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            if (sizeFix && GUILayout.Button($"Make it {row.NameSaysSize.name} ({row.NameSaysSize.height:0.#}px)", EditorStyles.miniButton))
            {
                row.Size = row.NameSaysSize;
                Replan();
            }
            if (iconFix && GUILayout.Button("Make it square", EditorStyles.miniButton))
            {
                row.Icon = true;
                Replan();
            }
            GUILayout.FlexibleSpace();
        }
    }

    // A fix button changes what the rest of this pass would draw, so the pass stops and redraws from the new plan.
    private void Replan()
    {
        StyleOrganiser.Refresh(plan, theme, ComponentKinds.Buttons);
        GUIUtility.ExitGUI();
    }

    private static string UseCaseControl(OrganiseRow row) => "usecase" + row.Style.GetInstanceID();

    private static void Indented(int depth, System.Action draw)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(depth * 16f + 58f);
            using (new EditorGUILayout.VerticalScope()) draw();
        }
    }

    private bool SizePopup(OrganiseRow row)
    {
        if (theme == null) return false;
        var options = new List<string> { row.Style.Height > 0f ? $"Own {row.Style.Height:0.#}px" : "Own height" };
        options.AddRange(theme.Sizes.Select(z => $"{z.name} {z.height:0.#}"));
        int current = row.Size == null ? 0 : theme.Sizes.ToList().IndexOf(row.Size) + 1;
        EditorGUI.BeginChangeCheck();
        int picked = EditorGUILayout.Popup(current, options.ToArray(), GUILayout.Width(90));
        if (!EditorGUI.EndChangeCheck()) return false;
        row.Size = picked == 0 ? null : theme.Sizes[picked - 1];
        return true;
    }

    private void DrawOrganiseFooter()
    {
        List<OrganiseRow> ready = plan.Where(r => r.Selected && r.Warning == null &&
                                                  (r.Action != OrganiseAction.Keep || r.NewName != r.Style.name)).ToList();
        int waiting = plan.Count(r => r.Selected && r.Warning != null);
        if (waiting > 0) EditorGUILayout.HelpBox($"{waiting} ticked row(s) have a warning and will be skipped.", MessageType.None);
        List<OrganiseRow> renames = plan.Where(r => r.Selected && r.Warning == null && r.NewName.Length > 0 && r.NewName != r.Style.name).ToList();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(renames.Count == 0))
                if (GUILayout.Button(new GUIContent($"Rename only ({renames.Count})",
                        "Give the ticked styles their new names and change nothing else: no linking, merging or deleting. Buttons keep their styles."),
                        GUILayout.Height(26)))
                    RenameOnly(renames);
            using (new EditorGUI.DisabledScope(ready.Count == 0))
                if (GUILayout.Button($"Apply {ready.Count} row(s)", GUILayout.Height(26))) ApplyOrganise(ready);
        }
    }

    private void RenameOnly(List<OrganiseRow> renames)
    {
        if (!EditorUtility.DisplayDialog("Rename styles",
                $"Rename {renames.Count} style(s):\n" + string.Join("\n", renames.Take(12).Select(r => $"• {r.Style.name} → {r.NewName}")) +
                (renames.Count > 12 ? $"\n… and {renames.Count - 12} more" : "") +
                "\n\nOnly the names change. Every button keeps its style.", "Rename", "Cancel"))
            return;
        StyleOrganiser.RenameOnly(plan, ComponentKinds.Buttons);
        Measure();
        GUIUtility.ExitGUI();
    }

    private void ApplyOrganise(List<OrganiseRow> ready)
    {
        int moved = ready.Where(r => r.Action == OrganiseAction.Merge).Sum(r => r.Uses);
        int looks = ready.Count(r => r.Changes.Count > 0);
        if (!EditorUtility.DisplayDialog("Organise styles",
                $"Change {ready.Count} style(s): {ready.Count(r => r.Action == OrganiseAction.Variant)} become variants, " +
                $"{ready.Count(r => r.Action == OrganiseAction.Merge)} merge ({moved} button(s) move), " +
                $"{ready.Count(r => r.Action == OrganiseAction.Delete)} are deleted, and the rest are renamed.\n\n" +
                (looks > 0 ? $"{looks} of them change how their buttons look — see each row's 'Looks different'.\n\n" : "No button changes how it looks.\n\n") +
                "This edits and saves prefabs and scenes and can't be undone from Unity — commit first.", "Apply", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var chosen = new HashSet<OrganiseRow>(ready);
        foreach (OrganiseRow row in plan) row.Selected = chosen.Contains(row);
        StyleOrganiser.Apply(plan, ComponentKinds.Buttons);
        Measure();
        GUIUtility.ExitGUI();
    }

    private void DrawGroup(StyleConsolidation.MergeGroup g)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Swatch(g.Keep, 20);
                EditorGUILayout.LabelField($"<b>{g.Family}</b> — {g.Members.Count} styles, {g.References} buttons", wrap);
            }

            string[] names = g.Members.Select(m => $"{Label(m)} ({m.References})").ToArray();
            int keepIndex = g.Members.IndexOf(g.Keep);
            int picked = EditorGUILayout.Popup("Keep", keepIndex, names);
            if (picked != keepIndex) { g.Keep = g.Members[picked]; excluded.Remove(g.Keep); }

            foreach (StyleUsage m in g.Members.Where(m => m != g.Keep))
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(m.Style.Locked))
                    {
                        bool include = EditorGUILayout.Toggle(!excluded.Contains(m) && !m.Style.Locked, GUILayout.Width(16));
                        if (include) excluded.Remove(m); else excluded.Add(m);
                    }
                    Swatch(m, 18);
                    EditorGUILayout.LabelField($"{Label(m)} · {m.References} button(s)");
                    if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50))) Select(m);
                    RemoveButton(m);
                }

            List<StyleUsage> merging = g.Members.Where(m => m != g.Keep && !excluded.Contains(m) && !m.Style.Locked).ToList();
            using (new EditorGUI.DisabledScope(merging.Count == 0))
                if (GUILayout.Button($"Merge {merging.Count} into {g.Keep.Style.name}")) Merge(merging, g.Keep);
        }
    }

    private void DrawManualRow(StyleUsage u)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            Swatch(u, 18);
            EditorGUILayout.LabelField($"{Label(u)} · {u.References} button(s) · {u.Family}");
            if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50))) Select(u);
            manualTargets.TryGetValue(u, out UIComponentStyle target);
            System.Type type = ComponentKinds.For(u.Style)?.StyleType ?? u.Style.GetType();
            target = (UIComponentStyle)EditorGUILayout.ObjectField(target, type, false, GUILayout.Width(150));
            manualTargets[u] = target;
            StyleUsage into = usages.FirstOrDefault(x => x.Style == target);
            using (new EditorGUI.DisabledScope(into == null || into == u))
                if (GUILayout.Button("Merge", EditorStyles.miniButton, GUILayout.Width(50))) Merge(new List<StyleUsage> { u }, into);
            RemoveButton(u);
        }
    }

    private void RemoveButton(StyleUsage u)
    {
        using var locked = new EditorGUI.DisabledScope(u.Style.Locked);
        if (!GUILayout.Button(new GUIContent("Remove", "Delete this style. Its buttons keep exactly how they look now, " +
                                                        "but stop following any style."), EditorStyles.miniButton, GUILayout.Width(60)))
            return;
        if (!EditorUtility.DisplayDialog("Remove style",
                $"Remove {u.Style.name}? Its {u.References} button(s) keep their current look but stop following any style.\n\n" +
                "This edits and saves their prefabs and scenes and can't be undone from Unity — commit first.", "Remove", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        StyleConsolidation.Remove(u);
        Measure();
        GUIUtility.ExitGUI();
    }

    private void Merge(List<StyleUsage> merging, StyleUsage keep)
    {
        int buttons = merging.Sum(m => m.References);
        int files = merging.SelectMany(m => m.Files).Distinct().Count();
        if (!EditorUtility.DisplayDialog("Merge styles",
                $"Restyle {buttons} button(s) in {files} file(s) as {keep.Style.name}, then remove:\n" +
                string.Join("\n", merging.Select(m => "• " + m.Style.name)) +
                "\n\nThis edits and saves those files and can't be undone from Unity — commit first.", "Merge", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        StyleConsolidation.Merge(merging, keep);
        Measure();
        GUIUtility.ExitGUI();
    }

    private static string Label(StyleUsage u) =>
        (u.Style.Locked ? "🔒 " : "") + (u.IsGenerated ? u.Style.name : $"{u.Style.name} (starter)") +
        (u.Style.IsVariant ? $" (variant of {u.Style.BaseStyle.name})" : "");

    private static void Select(StyleUsage u)
    {
        Selection.activeObject = u.Style;
        EditorGUIUtility.PingObject(u.Style);
    }

    // Colour on the left, shape on the right: drawing the sprite over the colour would hide the colour.
    private static void Swatch(StyleUsage u, float size)
    {
        Rect r = GUILayoutUtility.GetRect(size * 2f + 2f, size, GUILayout.Width(size * 2f + 2f), GUILayout.Height(size));
        EditorGUI.DrawRect(new Rect(r.x, r.y, size, size), u.Visible);
        Sprite sprite = u.Style.Background.ResolveSprite(u.Style.Theme);
        Texture preview = sprite != null ? AssetPreview.GetAssetPreview(sprite) : null;
        if (preview != null) GUI.DrawTexture(new Rect(r.x + size + 2f, r.y, size, size), preview, ScaleMode.ScaleToFit);
    }
}
