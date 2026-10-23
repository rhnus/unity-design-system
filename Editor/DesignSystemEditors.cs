using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws a <see cref="ThemeColor"/> as a dropdown of the theme's token names plus a swatch. Slash-separated names
/// become submenus. Resolves tokens through a sibling <c>theme</c> field when the object has one, otherwise the
/// project's single <see cref="UITheme"/>.
/// </summary>
[CustomPropertyDrawer(typeof(ThemeColor))]
public class ThemeColorDrawer : PropertyDrawer
{
    private const float SwatchWidth = 36f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty idProp = property.FindPropertyRelative(nameof(ThemeColor.tokenId));
        label = EditorGUI.BeginProperty(position, label, property);
        Rect field = EditorGUI.PrefixLabel(position, label);

        UITheme theme = ThemeFor(property);
        if (theme == null)
        {
            EditorGUI.LabelField(field, $"No theme yet — open {DesignSystemMenu.Path("Open Design System")}");
            EditorGUI.EndProperty();
            return;
        }

        var tokens = theme.Colors;
        string id = idProp.stringValue;
        bool missing = !string.IsNullOrEmpty(id) && theme.GetToken(id) == null;

        var names = new string[tokens.Count + (missing ? 2 : 1)];
        names[0] = "None";
        int current = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            names[i + 1] = string.IsNullOrEmpty(tokens[i].name) ? "(unnamed)" : tokens[i].name;
            if (tokens[i].id == id) current = i + 1;
        }
        if (missing)
        {
            names[^1] = "⚠ Missing token";
            current = names.Length - 1;
        }

        var popupRect = new Rect(field.x, field.y, field.width - SwatchWidth - 4f, field.height);
        var swatchRect = new Rect(field.xMax - SwatchWidth, field.y + 1f, SwatchWidth, field.height - 2f);

        EditorGUI.showMixedValue = idProp.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        int picked = EditorGUI.Popup(popupRect, current, names);
        if (EditorGUI.EndChangeCheck() && !(missing && picked == names.Length - 1))
            idProp.stringValue = picked == 0 ? string.Empty : tokens[picked - 1].id;
        EditorGUI.showMixedValue = false;

        if (theme.TryGetColor(idProp.stringValue, out Color c))
            EditorGUIUtility.DrawColorSwatch(swatchRect, c);

        EditorGUI.EndProperty();
    }

    private static UITheme ThemeFor(SerializedProperty property)
    {
        SerializedProperty themeProp = property.serializedObject.FindProperty("theme");
        if (themeProp != null && themeProp.objectReferenceValue is UITheme own) return own;
        return ProjectTheme;
    }

    private static UITheme cachedTheme;
    private static bool cacheValid;

    /// <summary>The project's single UITheme, cached until the project changes so drawers don't search per repaint.</summary>
    public static UITheme ProjectTheme
    {
        get
        {
            if (!cacheValid || cachedTheme == null)
            {
                cachedTheme = DesignSystemAssets.FindSingle<UITheme>();
                cacheValid = true;
            }
            return cachedTheme;
        }
    }

    [InitializeOnLoadMethod]
    private static void InvalidateOnProjectChange() => EditorApplication.projectChanged += () => cacheValid = false;
}

/// <summary>Fills an empty <c>theme</c> field with the project's UITheme, the way StyledText auto-wires its scale.</summary>
internal static class ThemeAutoWire
{
    public static void Wire(SerializedObject so)
    {
        SerializedProperty themeProp = so.FindProperty("theme");
        if (themeProp == null || themeProp.hasMultipleDifferentValues || themeProp.objectReferenceValue != null) return;
        UITheme theme = ThemeColorDrawer.ProjectTheme;
        if (theme == null) return;
        themeProp.objectReferenceValue = theme;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}

[CustomEditor(typeof(UITheme))]
public class UIThemeEditor : Editor
{
    private void OnEnable() => DesignSystemAssets.EnsureThemeTypography((UITheme)target);

    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
        {
            RecolorMaterials.Ensure((UITheme)target);
            DesignChangeLog.Note("theme");
            DesignSystemAssets.ReapplyOpen();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Design tokens", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Import reads W3C design-token JSON — what Figma's variable export, Tokens Studio and Style Dictionary " +
            "write. Tokens match by name, so re-importing updates values in place and every reference survives.",
            MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Import JSON…")) DesignTokenJson.ImportWithDialog((UITheme)target);
            if (GUILayout.Button("Export JSON…")) DesignTokenJson.ExportWithDialog((UITheme)target);
        }

    }
}

/// <summary>
/// Pieces shared by the style inspectors: named, collapsible sections, labelled fields with tooltips, the
/// "this can't look as intended" guidance, and the open-scene user count.
/// </summary>
public static class StyleInspectorParts
{
    /// <summary>A collapsible section header whose open state is remembered per section name.</summary>
    public static bool Section(string title, string tooltip, bool openByDefault)
    {
        string key = "DesignSystem.Section." + title;
        EditorGUILayout.Space(4);
        bool open = EditorGUILayout.BeginFoldoutHeaderGroup(SessionState.GetBool(key, openByDefault), new GUIContent(title, tooltip));
        EditorGUILayout.EndFoldoutHeaderGroup();
        SessionState.SetBool(key, open);
        return open;
    }

    /// <summary>The variant being drawn, if any: its overridden fields get a marker and "Revert to base".</summary>
    public static UIComponentStyle VariantContext;

    /// <summary>Fields edited this frame on a variant; the inspector marks them overridden once its changes are applied.</summary>
    public static readonly List<string> PendingOverrides = new List<string>();

    private static readonly Color OverrideColour = new Color(0.27f, 0.6f, 1f);

    public static void Field(SerializedProperty property, string label, string tooltip = null)
    {
        var content = new GUIContent(label, tooltip ?? property.tooltip);
        Rect r = EditorGUILayout.GetControlRect(true, EditorGUI.GetPropertyHeight(property, content, true));
        Tracked(r, property, content);
    }

    /// <summary>A label-less field of fixed width, for a second control on the same row.</summary>
    public static void InlineField(SerializedProperty property, float width)
    {
        Rect r = EditorGUILayout.GetControlRect(false, EditorGUI.GetPropertyHeight(property, GUIContent.none, true), GUILayout.Width(width));
        Tracked(r, property, GUIContent.none);
    }

    private static void Tracked(Rect r, SerializedProperty property, GUIContent content)
    {
        UIComponentStyle variant = VariantContext != null && VariantContext.IsVariant ? VariantContext : null;
        string path = property.propertyPath;
        bool overridden = variant != null && VariantSync.IsOverridden(variant, path);
        if (overridden)
        {
            EditorGUI.DrawRect(new Rect(r.x - 4f, r.y, 2f, r.height), OverrideColour);
            Event e = Event.current;
            if (e.type == EventType.ContextClick && r.Contains(e.mousePosition))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent($"Revert to {variant.BaseStyle.name}"), false, () => VariantSync.Revert(variant, path));
                menu.ShowAsContext();
                e.Use();
            }
        }
        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(r, property, content, true);
        if (EditorGUI.EndChangeCheck() && variant != null) PendingOverrides.Add(path);
    }

    /// <summary>Marks this frame's edits on a variant as overrides — after the edits are applied, so they aren't lost.</summary>
    public static void CommitOverrides(UIComponentStyle style)
    {
        foreach (string path in PendingOverrides) VariantSync.MarkOverridden(style, path);
        PendingOverrides.Clear();
    }

    /// <summary>A grey "→ result" line under a field, saying what it actually produces.</summary>
    public static void Result(string text) =>
        EditorGUILayout.LabelField(new GUIContent("→ " + text), EditorStyles.wordWrappedMiniLabel);

    // Each warning names what the designer will see go wrong, then offers the one change that fixes it.
    public static void DrawGuidance(SerializedObject so, SerializedProperty look)
    {
        var theme = so.FindProperty("theme").objectReferenceValue as UITheme;
        SerializedProperty shapeId = look.FindPropertyRelative("shape.tokenId");
        SerializedProperty plain = look.FindPropertyRelative("sprite");
        SerializedProperty mode = look.FindPropertyRelative("fillMode");
        UITheme.SpriteToken token = theme != null ? theme.GetSprite(shapeId.stringValue) : null;
        Sprite sprite = token != null && token.sprite != null ? token.sprite : plain.objectReferenceValue as Sprite;
        bool recolor = mode.enumValueIndex == (int)SurfaceFillMode.Recolor;

        if (recolor && sprite != null && !SurfaceLook.IsShape(sprite))
        {
            EditorGUILayout.HelpBox("This sprite is a picture, so Recolor would flatten it — it falls back to Tint.", MessageType.Warning);
        }
        else if (!recolor && sprite != null && SurfaceLook.IsShape(sprite) && RecolorMaterials.HasBakedColour(sprite)
                 && look.FindPropertyRelative("fill.tokenId").stringValue != "")
        {
            EditorGUILayout.HelpBox("This sprite has its own colour, so Tint can only darken it.", MessageType.Warning);
            if (GUILayout.Button(new GUIContent("Use Recolor", "Shows the fill colour as picked, keeping the sprite's shading.")))
                UseRecolor(so, theme, look, sprite);
        }
        else if (recolor && token == null)
        {
            EditorGUILayout.HelpBox("Recolor needs a sprite token — falling back to Tint.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(theme == null || sprite == null))
                if (GUILayout.Button("Create a sprite token from Sprite")) UseRecolor(so, theme, look, sprite);
        }

        if (look.FindPropertyRelative("cornerRadius").floatValue > 0f && sprite != null && sprite.border.x <= 0f
            && (token == null || token.sourceCornerRadius <= 0f))
            EditorGUILayout.HelpBox("This sprite has no slice border, so Corner has no effect.", MessageType.Info);
    }

    private static void UseRecolor(SerializedObject so, UITheme theme, SerializedProperty look, Sprite sprite)
    {
        if (theme == null || sprite == null) return;
        Undo.RecordObject(theme, "Use Recolor");
        UITheme.SpriteToken token = theme.GetOrAddSprite($"Shape/{sprite.name}", sprite);
        EditorUtility.SetDirty(theme);
        RecolorMaterials.Ensure(theme);
        look.FindPropertyRelative("shape.tokenId").stringValue = token.id;
        look.FindPropertyRelative("fillMode").enumValueIndex = (int)SurfaceFillMode.Recolor;
        so.ApplyModifiedProperties();
        DesignSystemAssets.ReapplyOpen();
    }

    /// <summary>"In open scenes: N · Select" for the components using this style.</summary>
    public static void OpenSceneUsers<T>(Object style, System.Func<T, Object> styleOf) where T : Component
    {
        T[] users = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .Where(c => styleOf(c) == style).ToArray();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(new GUIContent($"In open scenes: {users.Length}",
                "Buttons in prefabs and closed scenes are counted on the Design System window's Styles tab."), EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(users.Length == 0))
                if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(60)))
                    Selection.objects = users.Select(u => (Object)u.gameObject).ToArray();
        }
    }
}

/// <summary>
/// The panel style Inspector: the shared Look, plus the padding and spacing a panel's layout group gets. Label, size
/// and hover sections are hidden — a panel has none of them.
/// </summary>
[CustomEditor(typeof(UISurfaceStyle))]
public class UISurfaceStyleEditor : ComponentStyleEditor
{
    protected override void DrawKindSections(UIComponentStyle style)
    {
        if (!StyleInspectorParts.Section("Layout", "Padding and spacing written to a layout group on the same object.", false)) return;
        SerializedProperty apply = serializedObject.FindProperty("applyLayout");
        StyleInspectorParts.Field(apply, "Set padding & spacing");
        using (new EditorGUI.DisabledScope(!apply.boolValue))
        {
            StyleInspectorParts.Field(serializedObject.FindProperty("padding"), "Padding");
            StyleInspectorParts.Field(serializedObject.FindProperty("spacing"), "Spacing");
        }
    }
}

/// <summary>Styled components: re-apply as soon as a field changes so the Scene view follows the Inspector.</summary>
public abstract class StyledComponentEditor : Editor
{
    protected virtual void OnEnable() => ThemeAutoWire.Wire(serializedObject);

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUI.BeginChangeCheck();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        bool changed = EditorGUI.EndChangeCheck();
        serializedObject.ApplyModifiedProperties();
        if (changed)
            foreach (Object t in targets) DesignSystemAssets.ApplyOne((Component)t);

        DrawExtras();
    }

    protected virtual void DrawExtras() { }
}

[CustomEditor(typeof(StyledSelectable), true), CanEditMultipleObjects]
public class StyledSelectableEditor : StyledComponentEditor
{
    protected override void DrawExtras()
    {
        var first = (StyledSelectable)target;
        if (first.Background == null)
            EditorGUILayout.HelpBox("No visible Image was found for the background. Assign one above.", MessageType.Warning);
        if (first.ComponentStyle != null && first.ComponentStyle.ApplyLabelRole && first.Label != null && !first.Label.TryGetComponent(out StyledText _)
            && (first.ComponentStyle.Theme == null || first.ComponentStyle.Theme.Typography == null))
            EditorGUILayout.HelpBox("The theme has no type scale assigned, so the style's label font can't be applied. " +
                                    "Open the Design System window once to connect it.", MessageType.Info);

        var rect = (RectTransform)first.transform;
        if (first.ComponentStyle != null && first.ComponentStyle.Height > 0f && StyledSelectable.IsStretched(rect, vertical: true))
            EditorGUILayout.HelpBox("This button stretches vertically with its parent, so it keeps the parent's height " +
                                    "instead of the style's. Give it fixed vertical anchors to use the style height.", MessageType.Info);

        foreach (LayoutIssue issue in LayoutDiagnostics.Diagnose(rect, first.ComponentStyle))
        {
            EditorGUILayout.HelpBox(issue.Message, issue.Benign ? MessageType.Info : MessageType.Warning);
            if (issue.Fix != null && GUILayout.Button(issue.FixLabel))
            {
                issue.Fix();
                DesignSystemAssets.ApplyOne(first);
            }
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Auto-detect parts"))
                foreach (Object t in targets)
                {
                    DesignSystemAssets.Prepare((StyledSelectable)t);
                    DesignSystemAssets.ApplyOne((Component)t);
                }
            using (new EditorGUI.DisabledScope(targets.Length != 1 || first.Background == null || !(first is StyledButton)))
                if (GUILayout.Button("Save look as new style…")) ButtonStyleCapture.SaveAsStyle((StyledButton)first);
        }
    }
}

[CustomEditor(typeof(StyledSurface)), CanEditMultipleObjects]
public class StyledSurfaceEditor : StyledComponentEditor { }

[CustomEditor(typeof(StyledColor)), CanEditMultipleObjects]
public class StyledColorEditor : StyledComponentEditor { }

/// <summary>Turns a well-built existing button into a reusable <see cref="UIButtonStyle"/>.</summary>
public static class ButtonStyleCapture
{
    public static UIButtonStyle SaveAsStyle(StyledButton source)
    {
        string path = EditorUtility.SaveFilePanelInProject("Save Button Style", "ButtonStyle", "asset",
            "Captures this button's sprite, corner and colours into a new style.", DesignSystemAssets.DefaultFolder);
        if (string.IsNullOrEmpty(path)) return null;

        UIButtonStyle style = Capture(source.Background, source.Label, ThemeColorDrawer.ProjectTheme);
        AssetDatabase.CreateAsset(style, path);
        AssetDatabase.SaveAssets();

        Undo.RecordObject(source, "Use captured style");
        source.Style = style;
        DesignSystemAssets.MarkDirty(source);
        EditorGUIUtility.PingObject(style);
        return style;
    }

    /// <summary>
    /// A style reproducing the given parts. The corner is stored as a radius in canvas pixels, and colours as the
    /// theme's nearest token — a new "Unsorted/" token when none is close, for a designer to rename or merge.
    /// </summary>
    public static UIButtonStyle Capture(Image background, TMPro.TMP_Text label, UITheme theme)
    {
        var style = ScriptableObject.CreateInstance<UIButtonStyle>();
        style.Theme = theme;
        SurfaceLook look = style.Background;
        if (background != null)
        {
            look.sprite = background.sprite;
            if (theme != null && background.sprite != null)
                look.shape = ThemeSprite.From(theme.GetOrAddSprite($"Shape/{background.sprite.name}", background.sprite));
            look.imageType = background.type;
            look.fillCenter = background.fillCenter;
            look.pixelsPerUnitMultiplier = background.pixelsPerUnitMultiplier;
            if (background.sprite != null && background.sprite.border.x > 0f && background.type == Image.Type.Sliced)
                look.cornerRadius = Mathf.Round(background.sprite.border.x /
                                                (background.pixelsPerUnit * background.pixelsPerUnitMultiplier));
            look.fill = TokenFor(theme, background.color);
            style.Height = Mathf.Round(background.rectTransform.rect.height);
        }
        if (label != null) style.LabelColor = TokenFor(theme, label.color);
        return style;
    }

    public static ThemeColor TokenFor(UITheme theme, Color color)
    {
        if (theme == null) return default;
        UITheme.ColorToken nearest = null;
        float best = float.MaxValue;
        foreach (UITheme.ColorToken t in theme.Colors)
        {
            float d = ButtonScanner.ColorDistance(t.value, color);
            if (d < best) { best = d; nearest = t; }
        }
        if (nearest != null && best < 0.02f) return ThemeColor.From(nearest);

        Undo.RecordObject(theme, "Add colour token");
        UITheme.ColorToken added = theme.SetColor("Unsorted/" + ColorUtility.ToHtmlStringRGBA(color), color);
        EditorUtility.SetDirty(theme);
        return ThemeColor.From(added);
    }
}
