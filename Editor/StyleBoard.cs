using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The Styles tab: two rows of theme defaults, then one line per style — what its controls get, grey where it
/// follows the theme and bold where it sets its own — expanding to edit the few fields that matter in place. A kind
/// filter (All · Button · Toggle) appears once styles of more than one kind exist.
/// Explanations live in tooltips and rarer actions behind ⋯ menus, so the tab stays scannable.
/// </summary>
public class StyleBoard
{
    private readonly Dictionary<UIComponentStyle, SerializedObject> serialized = new Dictionary<UIComponentStyle, SerializedObject>();
    private readonly HashSet<UIComponentStyle> expanded = new HashSet<UIComponentStyle>();
    private IComponentKind kindFilter;
    private Dictionary<UIComponentStyle, int> usage;
    private string search = "";
    private GUIStyle rich, nameStyle;

    public void Draw(UITheme theme, List<UIComponentStyle> styles)
    {
        rich ??= new GUIStyle(EditorStyles.miniLabel) { richText = true };
        nameStyle ??= new GUIStyle(EditorStyles.label) { richText = true };

        DrawDefaults(theme, styles);
        EditorGUILayout.Space(6);
        DrawKindFilter(styles);

        using (new EditorGUILayout.HorizontalScope())
        {
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            if (GUILayout.Button(new GUIContent(usage == null ? "Count buttons" : "Recount",
                    "Count the buttons & toggles using each style across every prefab and scene, and sort by it."), EditorStyles.miniButton, GUILayout.Width(90)))
                usage = StyleConsolidation.MeasureUsage().ToDictionary(u => u.Style, u => u.References);
            if (GUILayout.Button(MenuIcon(), EditorStyles.label, GUILayout.Width(20))) ListMenu(theme, styles);
        }
        EditorGUILayout.LabelField(new GUIContent("Grey = from the theme · bold = set on the style",
            "Change a grey value in the theme defaults above; change a bold one on the style itself."), rich);

        IEnumerable<UIComponentStyle> shown = styles.Where(s => (kindFilter == null || ComponentKinds.For(s) == kindFilter)
            && (string.IsNullOrEmpty(search) || s.name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0));
        List<UIComponentStyle> list = shown.ToList();
        IEnumerable<UIComponentStyle> roots = list.Where(s => !s.IsVariant || !list.Contains(s.BaseStyle));
        roots = usage != null ? roots.OrderByDescending(s => usage.TryGetValue(s, out int n) ? n : 0).ThenBy(s => s.name) : roots.OrderBy(s => s.name);
        foreach (UIComponentStyle style in roots) DrawFamily(style, list, styles, 0);
    }

    private void DrawKindFilter(List<UIComponentStyle> styles)
    {
        List<IComponentKind> kinds = ComponentKinds.All.Where(k => styles.Any(s => ComponentKinds.For(s) == k)).ToList();
        if (kinds.Count < 2) { kindFilter = null; return; }
        kinds.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
        string[] labels = new[] { $"All {styles.Count}" }
            .Concat(kinds.Select(k => $"{k.DisplayName} {styles.Count(s => ComponentKinds.For(s) == k)}")).ToArray();
        int index = GUILayout.Toolbar(kindFilter == null ? 0 : kinds.IndexOf(kindFilter) + 1, labels, EditorStyles.miniButton);
        kindFilter = index == 0 ? null : kinds[index - 1];
    }

    private void ListMenu(UITheme theme, List<UIComponentStyle> styles)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Preview all styles"), false, () =>
        {
            DesignSystemStarterKit.BuildPreview();
            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(DesignSystemStarterKit.PreviewPath));
        });
        menu.AddItem(new GUIContent("Name generated styles by role…"), false, StyleRenamer.NameGeneratedByRole);
        menu.AddItem(new GUIContent(expanded.Count > 0 ? "Collapse all" : "Expand all"), false, () =>
        {
            if (expanded.Count > 0) expanded.Clear();
            else foreach (UIComponentStyle s in styles) expanded.Add(s);
        });
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Colours & shapes (theme)"), false, () => Selection.activeObject = theme);
        menu.AddItem(new GUIContent("Type scale"), false, () => Selection.activeObject = theme.Typography);
        menu.AddItem(new GUIContent("Import colours from Figma…"), false, () => DesignTokenJson.ImportWithDialog(theme));
        menu.ShowAsContext();
    }

    // ---- Theme defaults ----

    private void DrawDefaults(UITheme theme, List<UIComponentStyle> styles)
    {
        DesignSystemAssets.EnsureThemeTypography(theme);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(new GUIContent("Theme defaults", "Every style set to 'Use theme' follows these."), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Height", "Button height in px. 0 = buttons keep their own."), GUILayout.Width(60));
                EditorGUI.BeginChangeCheck();
                float height = EditorGUILayout.DelayedFloatField(theme.ButtonHeight, GUILayout.Width(60));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(theme, "Button height");
                    theme.ButtonHeight = Mathf.Max(0f, height);
                    Saved(theme, $"Button height → {theme.ButtonHeight:0.#} px");
                }
                EditorGUILayout.LabelField("px", GUILayout.Width(20));
                List<UIComponentStyle> controls = styles.Where(s => ComponentKinds.For(s)?.IsControl != false).ToList();
                Following(controls.Count(s => s.HeightSource == ButtonHeightSource.Theme), controls.Count);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Text", "Off = each label keeps its own font. On = labels use this type scale role."), GUILayout.Width(60));
                EditorGUI.BeginChangeCheck();
                bool on = EditorGUILayout.Toggle(theme.SetButtonLabelFont, GUILayout.Width(16));
                string role;
                using (new EditorGUI.DisabledScope(!on))
                    role = TextRoleField.Popup(theme.ButtonLabelRoleId, theme.Typography, GUILayout.Width(110));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(theme, "Button text");
                    theme.SetButtonLabelFont = on;
                    theme.ButtonLabelRoleId = role;
                    Saved(theme, on ? $"Button text → {role}" : "Button text → own fonts");
                }

                TypographyScale scale = theme.Typography;
                TypographyScale.RoleStyle rs = on && scale != null ? scale.Get(theme.ButtonLabelRoleId) : null;
                if (rs != null)
                {
                    EditorGUI.BeginChangeCheck();
                    float size = EditorGUILayout.DelayedFloatField(new GUIContent("", $"Size of the '{theme.ButtonLabelRoleId}' role — other text using it changes too."),
                                                                   rs.fontSize, GUILayout.Width(40));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(scale, "Button text size");
                        rs.fontSize = Mathf.Max(1f, size);
                        EditorUtility.SetDirty(scale);
                        AssetDatabase.SaveAssetIfDirty(scale);
                        TypographyAssets.ReapplyEverywhere();
                        DesignChangeLog.Note($"{theme.ButtonLabelRoleId} text → {rs.fontSize:0.#} px");
                        DesignSystemAssets.ReapplyOpen();
                    }
                    EditorGUILayout.LabelField("px", GUILayout.Width(20));
                }
                List<UIComponentStyle> labelled = styles.Where(s => ComponentKinds.For(s)?.IsControl != false).ToList();
                Following(labelled.Count(s => s.LabelFontSource == LabelFontSource.Theme), labelled.Count);
            }
        }
    }

    private void Following(int following, int total) =>
        EditorGUILayout.LabelField(new GUIContent($"<color=#8C8C8C>{following}/{total} styles follow</color>",
            "Styles set to 'Use theme'. The rest set their own value on their card."), rich);

    private static void Saved(UITheme theme, string change)
    {
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssetIfDirty(theme);
        DesignChangeLog.Note(change);
        DesignSystemAssets.ReapplyOpen();
    }

    // ---- Style rows ----

    // A style, then its variants indented beneath it.
    private void DrawFamily(UIComponentStyle style, List<UIComponentStyle> shown, List<UIComponentStyle> styles, int depth)
    {
        DrawStyle(style, styles, depth);
        if (depth > 4) return;
        foreach (UIComponentStyle variant in shown.Where(s => s.BaseStyle == style).OrderBy(s => s.name))
            DrawFamily(variant, shown, styles, depth + 1);
    }

    private void DrawStyle(UIComponentStyle style, List<UIComponentStyle> styles, int depth)
    {
        using (new EditorGUILayout.HorizontalScope())
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            bool open = expanded.Contains(style);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(open ? "▾" : "▸", EditorStyles.label, GUILayout.Width(12)))
                {
                    if (open) expanded.Remove(style); else expanded.Add(style);
                    open = !open;
                }
                if (depth > 0) GUILayout.Space(14f * depth);
                Swatch(style);
                string count = usage == null ? "" : $" <color=#8C8C8C>· {(usage.TryGetValue(style, out int n) ? n : 0)}</color>";
                string prefix = depth > 0 ? "↳ " : "";
                if (style is UIDragCardStyle && style == DragCardStandard.Find()) prefix += "★ ";
                EditorGUILayout.LabelField(new GUIContent(prefix + style.name + count,
                    style.IsVariant ? $"Variant of {style.BaseStyle.name}. Controls using it shown after the name, once counted."
                                    : "Controls using it shown after the name, once counted."), nameStyle);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(DesignSystemDescribe.Short(style), rich, GUILayout.Width(150));
                if (style.Locked)
                    GUILayout.Label(new GUIContent(EditorGUIUtility.IconContent("IN LockButton on").image, "Locked — unlock it in its Inspector to edit."),
                                    GUILayout.Width(18));
                if (DesignSystemRepair.NeedsRepair(style))
                    GUILayout.Label(new GUIContent(EditorGUIUtility.IconContent("console.warnicon.sml").image,
                        "Treats a picture like a button shape — fix it on the Check tab."), GUILayout.Width(18));
                if (GUILayout.Button(MenuIcon(), EditorStyles.label, GUILayout.Width(20))) RowMenu(style, styles);
            }
            if (open) DrawFields(style);
        }
    }

    private void DrawFields(UIComponentStyle style)
    {
        using var locked = new EditorGUI.DisabledScope(style.Locked);
        SerializedObject so = Serialized(style);
        so.Update();
        StyleInspectorParts.VariantContext = style;
        StyleInspectorParts.PendingOverrides.Clear();
        float labelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 80f;
        EditorGUI.BeginChangeCheck();

        SerializedProperty look = so.FindProperty("background");
        IComponentKind kind = ComponentKinds.For(style);
        bool control = kind?.IsControl != false;
        StyleInspectorParts.Field(look.FindPropertyRelative("fill"), "Fill");
        foreach ((string path, string label) in kind?.QuickFields ?? Enumerable.Empty<(string, string)>())
            StyleInspectorParts.Field(so.FindProperty(path), label);
        if (control) StyleInspectorParts.Field(so.FindProperty("labelColor"), "Label");
        StyleInspectorParts.Field(look.FindPropertyRelative("cornerRadius"), "Corner (px)");
        if (control)
        using (new EditorGUILayout.HorizontalScope())
        {
            SerializedProperty font = so.FindProperty("labelFontSource");
            StyleInspectorParts.Field(font, "Text");
            if ((LabelFontSource)font.enumValueIndex == LabelFontSource.Custom)
                StyleInspectorParts.InlineField(so.FindProperty("labelRoleId"), 110f);
        }
        if (control)
        using (new EditorGUILayout.HorizontalScope())
        {
            SerializedProperty height = so.FindProperty("heightSource");
            StyleInspectorParts.Field(height, "Height");
            if ((ButtonHeightSource)height.enumValueIndex == ButtonHeightSource.Custom)
                StyleInspectorParts.InlineField(so.FindProperty("height"), 60f);
            else if ((ButtonHeightSource)height.enumValueIndex == ButtonHeightSource.NamedSize)
                StyleInspectorParts.InlineField(so.FindProperty("size"), 110f);
        }

        bool changed = EditorGUI.EndChangeCheck();
        EditorGUIUtility.labelWidth = labelWidth;
        StyleInspectorParts.VariantContext = null;
        if (!changed) { StyleInspectorParts.PendingOverrides.Clear(); return; }
        so.ApplyModifiedProperties();
        StyleInspectorParts.CommitOverrides(style);
        DesignChangeLog.Note(style.name);
        DesignSystemAssets.ReapplyOpen();
    }

    private void RowMenu(UIComponentStyle style, List<UIComponentStyle> styles)
    {
        var menu = new GenericMenu();
        IComponentKind kind = ComponentKinds.For(style);
        string prefix = (kind?.DisplayName ?? "Button") + "_";
        menu.AddItem(new GUIContent("Edit all settings"), false, () => Selection.activeObject = style);
        menu.AddItem(new GUIContent("Create variant…"), false, () => NewVariantWindow.Open(style));
        if (style is UIDragCardStyle card)
        {
            UIDragCardStyle standard = DragCardStandard.Find();
            if (standard == card) menu.AddDisabledItem(new GUIContent("Standard drag card ✓"));
            else
            {
                menu.AddItem(new GUIContent("Set as the standard drag card"), false, () => DragCardStandard.Set(card));
                if (standard != null && !DragCardStandard.Follows(card, standard))
                    menu.AddItem(new GUIContent("Follow the standard handle"), false, () => FollowStandard(card, standard));
            }
        }
        foreach (string role in StyleRenamer.Roles)
            menu.AddItem(new GUIContent("Rename to role/" + role), false, () => StyleRenamer.Rename(style, prefix + role));
        menu.AddItem(new GUIContent("Rename to role/Custom name…"), false, () => StyleRenamer.Prompt(style));
        if (style.Locked)
        {
            menu.AddDisabledItem(new GUIContent("Merge into (locked)"));
            menu.AddDisabledItem(new GUIContent("Remove style… (locked)"));
        }
        else
        {
            foreach (UIComponentStyle other in styles.Where(s => s != style && ComponentKinds.For(s) == kind).OrderBy(s => s.name))
                menu.AddItem(new GUIContent("Merge into/" + other.name), false, () => MergeInto(style, other));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Remove style…"), false, () => Remove(style));
        }
        menu.ShowAsContext();
    }

    private static void FollowStandard(UIDragCardStyle card, UIDragCardStyle standard)
    {
        List<string> changes = DragCardStandard.Differences(card, standard);
        string detail = changes.Count == 0 ? "Its cards won't change on screen." : "Its cards will change:\n• " + string.Join("\n• ", changes);
        if (!EditorUtility.DisplayDialog("Follow the standard handle",
                $"{card.name} will take its handle from {standard.name}, now and whenever the standard " +
                $"changes. Everything else stays its own.\n\n{detail}", "Follow", "Cancel")) return;
        DragCardStandard.Follow(card, standard);
    }

    private void MergeInto(UIComponentStyle style, UIComponentStyle keep)
    {
        List<StyleUsage> all = StyleConsolidation.MeasureUsage();
        StyleUsage from = all.First(u => u.Style == style), into = all.First(u => u.Style == keep);
        if (!EditorUtility.DisplayDialog("Merge style",
                $"Restyle {from.References} buttons & toggles as {keep.name} and remove {style.name}?\n\n" +
                "This edits and saves their prefabs and scenes — commit first.", "Merge", "Cancel")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        StyleConsolidation.Merge(new List<StyleUsage> { from }, into);
        usage = null;
    }

    private void Remove(UIComponentStyle style)
    {
        StyleUsage u = StyleConsolidation.MeasureUsage().First(x => x.Style == style);
        if (!EditorUtility.DisplayDialog("Remove style",
                $"Remove {style.name}? Its {u.References} buttons & toggles keep their current look but stop following any style.\n\n" +
                "This edits and saves their prefabs and scenes — commit first.", "Remove", "Cancel")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        StyleConsolidation.Remove(u);
        usage = null;
    }

    private SerializedObject Serialized(UIComponentStyle style)
    {
        if (!serialized.TryGetValue(style, out SerializedObject so) || so.targetObject == null)
            serialized[style] = so = new SerializedObject(style);
        return so;
    }

    private static GUIContent MenuIcon() => new GUIContent(EditorGUIUtility.IconContent("_Menu").image, "More");

    // Colour on the left, shape on the right: drawing the sprite over the colour would hide the colour. A style with
    // an On state splits the colour square: Off, then On.
    private static void Swatch(UIComponentStyle style)
    {
        const float size = 16f;
        Rect r = GUILayoutUtility.GetRect(size * 2f + 2f, size, GUILayout.Width(size * 2f + 2f), GUILayout.Height(size));
        EditorGUI.DrawRect(new Rect(r.x, r.y, size, size), StyleConsolidation.VisibleColour(style));
        if (style.HasOnState) EditorGUI.DrawRect(new Rect(r.x + size / 2f, r.y, size / 2f, size), StyleConsolidation.VisibleColour(style, true));
        Sprite sprite = style.Background.ResolveSprite(style.Theme);
        Texture preview = sprite != null ? AssetPreview.GetAssetPreview(sprite) : null;
        if (preview != null) GUI.DrawTexture(new Rect(r.x + size + 2f, r.y, size, size), preview, ScaleMode.ScaleToFit);
    }
}
