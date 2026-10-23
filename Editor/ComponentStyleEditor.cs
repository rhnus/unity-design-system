using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Inspector for every control style, laid out the way a style is thought about: a summary of what its buttons get,
/// then Look, Label and Size open, and the settings few styles need (hover tints, icon, image details) in named
/// sections below. Every theme-driven value shows what it resolves to, and the label's text size can be edited
/// right here — it is the type scale role's size, shared by everything using that role.
/// </summary>
[CustomEditor(typeof(UIComponentStyle), true)]
public class ComponentStyleEditor : Editor
{
    private const float PreviewW = 200f, StatePreviewW = 150f, PreviewH = 64f;

    private int stylesSharingRole = -1;
    private string sharedRole;
    private ButtonPreviewRenderer renderer;
    private RenderTexture before;
    private ButtonPreviewRenderer.Spec beforeSpec;
    private bool changedSinceOpen;
    private List<string> usedIn;
    private bool showUsedIn;

    private void OnEnable()
    {
        ThemeAutoWire.Wire(serializedObject);
        var style = (UIComponentStyle)target;
        DesignSystemAssets.EnsureThemeTypography(style.Theme);
        renderer = new ButtonPreviewRenderer();
        beforeSpec = ButtonPreviewRenderer.Capture(style);
    }

    private void OnDisable()
    {
        renderer?.Dispose();
        renderer = null;
        if (before != null) { before.Release(); DestroyImmediate(before); }
    }

    public override void OnInspectorGUI()
    {
        var style = (UIComponentStyle)target;
        serializedObject.Update();
        StyleInspectorParts.VariantContext = style;
        StyleInspectorParts.PendingOverrides.Clear();
        DrawSummary(style);

        SerializedProperty look = serializedObject.FindProperty("background");
        EditorGUI.BeginDisabledGroup(style.Locked);
        EditorGUI.BeginChangeCheck();

        DrawKindHeader(style);

        if (StyleInspectorParts.Section("Look", "The background: shape, colour and corner.", true))
        {
            SerializedProperty shape = look.FindPropertyRelative("shape");
            StyleInspectorParts.Field(shape, "Shape", "A shape sprite from the theme. Change the theme's sprite and every style using it follows.");
            if (string.IsNullOrEmpty(shape.FindPropertyRelative("tokenId").stringValue))
                StyleInspectorParts.Field(look.FindPropertyRelative("sprite"), "Sprite", "Used because no Shape is picked. Empty = keep each button's sprite.");
            StyleInspectorParts.Field(look.FindPropertyRelative("fillMode"), "Fill mode");
            StyleInspectorParts.Field(look.FindPropertyRelative("fill"), "Fill");
            StyleInspectorParts.Field(look.FindPropertyRelative("cornerRadius"), "Corner (px)",
                "Corner radius in pixels, as in Figma. 0 = use the PPU multiplier under Rarely needed.");
            StyleInspectorParts.DrawGuidance(serializedObject, look);
        }

        bool labelSizeEdited = false;
        bool control = IsControl(style);
        if (control && StyleInspectorParts.Section("Label", "The text: colour, font and side padding.", true))
        {
            StyleInspectorParts.Field(serializedObject.FindProperty("labelColor"), "Colour");
            using (new EditorGUILayout.HorizontalScope())
            {
                SerializedProperty source = serializedObject.FindProperty("labelFontSource");
                StyleInspectorParts.Field(source, "Font");
                if ((LabelFontSource)source.enumValueIndex == LabelFontSource.Custom)
                    StyleInspectorParts.InlineField(serializedObject.FindProperty("labelRoleId"), 120f);
            }
            StyleInspectorParts.Result(DesignSystemDescribe.LabelFont(style));
            labelSizeEdited = DrawLabelSize(style);
            StyleInspectorParts.Field(serializedObject.FindProperty("labelSize"), "Text size (px)",
                "This style's own text size, e.g. smaller for icon + text buttons. 0 = the role's size.");
            StyleInspectorParts.Field(serializedObject.FindProperty("labelPadding"), "Side padding (px)",
                "Space between the label and the button's left and right edges. -1 = keep each button's own.");
        }

        if (control && StyleInspectorParts.Section("Size", "Height and width. Works inside layout groups and content size fitters.", true))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                SerializedProperty source = serializedObject.FindProperty("heightSource");
                StyleInspectorParts.Field(source, "Height");
                if ((ButtonHeightSource)source.enumValueIndex == ButtonHeightSource.Custom)
                    StyleInspectorParts.InlineField(serializedObject.FindProperty("height"), 60f);
                else if ((ButtonHeightSource)source.enumValueIndex == ButtonHeightSource.NamedSize)
                    StyleInspectorParts.InlineField(serializedObject.FindProperty("size"), 110f);
            }
            if (style.Height > 0f) StyleInspectorParts.Field(serializedObject.FindProperty("growWithContent"), "Grow with text");
            StyleInspectorParts.Result(DesignSystemDescribe.Height(style));
            if (ShowsWidth) DrawWidth();
        }

        DrawKindSections(style);

        if (control && ComponentKinds.For(style)?.HasStateTints != false && StyleInspectorParts.Section("Hover & press", "How the button reacts when pointed at, pressed or disabled.", false))
        {
            SerializedProperty tints = serializedObject.FindProperty("applyStateTints");
            StyleInspectorParts.Field(tints, "Use these tints", "Off = leave each button's own transition, e.g. buttons that swap sprites.");
            using (new EditorGUI.DisabledScope(!tints.boolValue))
            {
                StyleInspectorParts.Field(serializedObject.FindProperty("highlightedTint"), "Hover", "Multiplied over the fill.");
                StyleInspectorParts.Field(serializedObject.FindProperty("pressedTint"), "Pressed", "Multiplied over the fill.");
                StyleInspectorParts.Field(serializedObject.FindProperty("selectedTint"), "Selected", "Multiplied over the fill.");
                StyleInspectorParts.Field(serializedObject.FindProperty("disabledTint"), "Disabled", "Multiplied over the fill.");
                StyleInspectorParts.Field(serializedObject.FindProperty("fadeDuration"), "Fade (s)");
            }
        }

        if (control && ShowsIcon && StyleInspectorParts.Section("Icon", "Colour for an icon image on the button, if the button has one assigned.", false))
            StyleInspectorParts.Field(serializedObject.FindProperty("iconColor"), "Icon colour");

        if (StyleInspectorParts.Section("Rarely needed", "Image details most styles never touch.", false))
        {
            StyleInspectorParts.Field(serializedObject.FindProperty("theme"), "Theme");
            if (!string.IsNullOrEmpty(look.FindPropertyRelative("shape.tokenId").stringValue))
                StyleInspectorParts.Field(look.FindPropertyRelative("sprite"), "Fallback sprite", "Used only if the Shape token is removed.");
            StyleInspectorParts.Field(look.FindPropertyRelative("imageType"), "Image type");
            StyleInspectorParts.Field(look.FindPropertyRelative("fillCenter"), "Fill centre");
            StyleInspectorParts.Field(look.FindPropertyRelative("pixelsPerUnitMultiplier"), "PPU multiplier", "Used only when Corner is 0.");
            if (control && !ShowsWidth) DrawWidth();
            if (control && !ShowsIcon) StyleInspectorParts.Field(serializedObject.FindProperty("iconColor"), "Icon colour");
            if (control && GUILayout.Button(new GUIContent("Import spec from Figma…", "Copy height, corner, colours and text size from a Figma button."),
                                            EditorStyles.miniButton))
                FigmaButtonImportWindow.Open(style);
        }

        bool changed = EditorGUI.EndChangeCheck() && !labelSizeEdited;
        EditorGUI.EndDisabledGroup();
        serializedObject.ApplyModifiedProperties();
        StyleInspectorParts.CommitOverrides(style);
        StyleInspectorParts.VariantContext = null;
        if (changed)
        {
            stylesSharingRole = -1;
            changedSinceOpen = true;
            DesignChangeLog.Note(target.name);
            DesignSystemAssets.ReapplyOpen();
        }
        if (labelSizeEdited) changedSinceOpen = true;

        EditorGUILayout.Space(6);
        StyleInspectorParts.OpenSceneUsers<StyledElement>(target, c => c.ComponentStyle);
        DrawUsedIn(style);
    }

    /// <summary>
    /// Sections only this kind of control has — e.g. a toggle's On/Off states and checkmark — drawn after the shared
    /// Look, Label and Size sections. Fields drawn through <see cref="StyleInspectorParts.Field"/> get variant markers.
    /// </summary>
    protected virtual void DrawKindSections(UIComponentStyle style) { }

    /// <summary>Whether the style's elements have a label, size and hover states; panels have only a look.</summary>
    protected static bool IsControl(UIComponentStyle style) => ComponentKinds.For(style)?.IsControl != false;

    /// <summary>A kind's own first row, above Look — e.g. a toggle's type, which decides the sections below.</summary>
    protected virtual void DrawKindHeader(UIComponentStyle style) { }

    /// <summary>Whether width, min width and square sit in Size; otherwise they move to Rarely needed.</summary>
    protected virtual bool ShowsWidth => true;

    /// <summary>Whether the Icon section shows; otherwise its colour moves to Rarely needed.</summary>
    protected virtual bool ShowsIcon => true;

    private void DrawWidth()
    {
        StyleInspectorParts.Field(serializedObject.FindProperty("square"), "Square (width = height)", "For icon-only controls.");
        StyleInspectorParts.Field(serializedObject.FindProperty("width"), "Width (px)", "0 = keep each control's own width.");
        StyleInspectorParts.Field(serializedObject.FindProperty("minWidth"), "Min width (px)", "Widens narrower controls, never shrinks. 0 = off.");
    }

    private void StatePreview(UIComponentStyle style, bool on)
    {
        Rect rect = GUILayoutUtility.GetRect(StatePreviewW, PreviewH, GUILayout.Width(StatePreviewW), GUILayout.Height(PreviewH));
        renderer?.Draw(rect, style, on);
        GUI.Label(new Rect(rect.x + 4f, rect.yMax - 15f, 30f, 14f), on ? "On" : "Off", EditorStyles.miniLabel);
    }

    // A real render of the control; once something changes, the look when the Inspector opened sits beside it.
    // Styles with an On state show Off and On instead.
    private void DrawSummary(UIComponentStyle style)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (style.HasOnState)
                {
                    StatePreview(style, false);
                    GUILayout.Space(6);
                    StatePreview(style, true);
                }
                else if (changedSinceOpen)
                {
                    Rect beforeRect = GUILayoutUtility.GetRect(PreviewW, PreviewH, GUILayout.Width(PreviewW), GUILayout.Height(PreviewH));
                    if (renderer.UsesRender && before != null)
                    {
                        if (Event.current.type == EventType.Repaint) GUI.DrawTexture(beforeRect, before, ScaleMode.StretchToFill, false);
                    }
                    else ButtonPreviewRenderer.DrawSpec(beforeRect, beforeSpec);
                    GUI.Label(new Rect(beforeRect.x + 4f, beforeRect.yMax - 15f, 50f, 14f), "Before", EditorStyles.miniLabel);
                    GUILayout.Space(6);
                }
                if (!style.HasOnState)
                {
                    Rect nowRect = GUILayoutUtility.GetRect(PreviewW, PreviewH, GUILayout.Width(PreviewW), GUILayout.Height(PreviewH));
                    Texture now = renderer?.Draw(nowRect, style);
                    if (now != null && before == null) before = ButtonPreviewRenderer.Snapshot(now);
                    if (changedSinceOpen) GUI.Label(new Rect(nowRect.x + 4f, nowRect.yMax - 15f, 40f, 14f), "Now", EditorStyles.miniLabel);
                }
                GUILayout.FlexibleSpace();
            }
            if (style.IsVariant)
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent($"Variant of {style.BaseStyle.name} · {style.Overrides.Count} override(s)",
                        "Follows its base except the fields marked blue. Right-click a marked field to revert it."), EditorStyles.miniBoldLabel);
                    if (GUILayout.Button("Select base", EditorStyles.miniButton, GUILayout.Width(80))) Selection.activeObject = style.BaseStyle;
                    using (new EditorGUI.DisabledScope(style.Overrides.Count == 0 || style.Locked))
                        if (GUILayout.Button("Revert all", EditorStyles.miniButton, GUILayout.Width(70))) VariantSync.RevertAll(style);
                }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    if (IsControl(style))
                    {
                        EditorGUILayout.LabelField("Text   " + DesignSystemDescribe.LabelFont(style), EditorStyles.miniLabel);
                        EditorGUILayout.LabelField("Height " + DesignSystemDescribe.Height(style), EditorStyles.miniLabel);
                    }
                }
                if (GUILayout.Button(new GUIContent("Create variant…", "A new style that follows this one except what you change — " +
                                                                         "e.g. smaller text for icon + text, or square for icon only."),
                                     EditorStyles.miniButton, GUILayout.Width(100)))
                    NewVariantWindow.Open(style);
                var lockContent = new GUIContent(EditorGUIUtility.IconContent(style.Locked ? "IN LockButton on" : "IN LockButton").image,
                    style.Locked ? "Locked: the tools won't edit, merge, remove or repair it. Theme defaults it follows still apply. Click to unlock."
                                 : "Lock this approved style so it can't be changed by accident.");
                bool locked = GUILayout.Toggle(style.Locked, lockContent, EditorStyles.label, GUILayout.Width(20));
                if (locked != style.Locked)
                {
                    Undo.RecordObject(style, locked ? "Lock style" : "Unlock style");
                    style.Locked = locked;
                    EditorUtility.SetDirty(style);
                }
            }
        }
    }

    // Every prefab and scene that uses the style, read from the files on request rather than on every repaint.
    private void DrawUsedIn(UIComponentStyle style)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent(usedIn == null ? "Find everywhere" : $"Used in {usedIn.Count} file(s)",
                    "List every prefab and scene using this style."), EditorStyles.miniButton))
            {
                if (usedIn == null) usedIn = StyleConsolidation.MeasureUsage().FirstOrDefault(u => u.Style == style)?.Files ?? new List<string>();
                showUsedIn = !showUsedIn || usedIn.Count == 0;
            }
            if (style.ControlsSize &&
                GUILayout.Button(new GUIContent("Update prefabs", "Write this style's size into prefab files too, so the Scene view matches everywhere. " +
                                                                  "Play mode and builds already get it."), EditorStyles.miniButton))
                DesignSystemAssets.ReapplyProject();
        }
        if (!showUsedIn || usedIn == null) return;
        foreach (string file in usedIn)
            if (GUILayout.Button(file, EditorStyles.linkLabel))
                EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(file));
    }

    /// <summary>
    /// The label's text size, edited where people look for it. It is the type scale role's size, so the field says
    /// how many styles share it. Returns whether the size was edited, which is a type scale change, not a style one.
    /// </summary>
    private bool DrawLabelSize(UIComponentStyle style)
    {
        UITheme theme = style.Theme;
        if (!style.TryGetLabelRoleId(out string role))
        {
            if (style.LabelFontSource == LabelFontSource.Theme && theme != null && !theme.SetButtonLabelFont)
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Each label keeps its own size.", EditorStyles.miniLabel);
                    if (GUILayout.Button(new GUIContent("Give all buttons one text size",
                            "Turns on the theme's button text, so every style using the theme font gets the same role and size."), EditorStyles.miniButton))
                    {
                        Undo.RecordObject(theme, "Button text");
                        theme.SetButtonLabelFont = true;
                        EditorUtility.SetDirty(theme);
                        AssetDatabase.SaveAssetIfDirty(theme);
                        DesignChangeLog.Note($"Button text → {theme.ButtonLabelRoleId}");
                        DesignSystemAssets.ReapplyOpen();
                        return true;
                    }
                }
            return false;
        }

        TypographyScale scale = theme != null ? theme.Typography : null;
        TypographyScale.RoleStyle rs = scale != null ? scale.Get(role) : null;
        if (rs == null) return false;

        bool edited = false;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            float size = EditorGUILayout.DelayedFloatField(new GUIContent($"{role} size (px)",
                $"The size of the '{role}' text role in the type scale."), rs.fontSize);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(scale, "Text size");
                rs.fontSize = Mathf.Max(1f, size);
                EditorUtility.SetDirty(scale);
                AssetDatabase.SaveAssetIfDirty(scale);
                TypographyAssets.ReapplyEverywhere();
                DesignChangeLog.Note($"{role} text → {rs.fontSize:0.#} px");
                DesignSystemAssets.ReapplyOpen();
                stylesSharingRole = -1;
                edited = true;
            }
            if (GUILayout.Button(new GUIContent("Type scale", "Open the type scale: font, weight and spacing of every role."),
                                 EditorStyles.miniButton, GUILayout.Width(80)))
                Selection.activeObject = scale;
        }

        if (stylesSharingRole < 0 || sharedRole != role)
        {
            sharedRole = role;
            stylesSharingRole = DesignSystemAssets.FindAll<UIComponentStyle>().Count(s => s.TryGetLabelRoleId(out string r) && r == role);
        }
        EditorGUILayout.LabelField($"Shared by {stylesSharingRole} button style(s) and any other text using '{role}'.", EditorStyles.wordWrappedMiniLabel);
        return edited;
    }
}
