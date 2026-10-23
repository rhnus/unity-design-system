using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared editor helpers: finding the design-system assets and pushing styles onto scenes and prefabs.</summary>
public static class DesignSystemAssets
{
    private static string cachedFolder;

    /// <summary>
    /// The folder the tools create assets in: the one holding the project's Design System Settings, so a project's
    /// design system stays together wherever it put it; <see cref="DesignSystemNames.DefaultAssetFolder"/> before
    /// there are settings.
    /// </summary>
    public static string DefaultFolder => cachedFolder ??= SettingsFolder() ?? DesignSystemNames.DefaultAssetFolder;

    /// <summary>Re-reads <see cref="DefaultFolder"/> next time, after settings are created or moved.</summary>
    public static void ForgetFolder() => cachedFolder = null;

    private static string SettingsFolder()
    {
        string path = AssetDatabase.GetAssetPath(FindSingle<DesignSystemSettings>());
        return string.IsNullOrEmpty(path) ? null : System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
    }

    /// <summary>The single asset of type T in the project, or null. Warns when there is more than one.</summary>
    public static T FindSingle<T>() where T : Object
    {
        string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
        if (guids.Length == 0) return null;
        if (guids.Length > 1)
            Debug.LogWarning($"[DesignSystem] {guids.Length} {typeof(T).Name} assets exist — auto-wiring picks " +
                             $"{AssetDatabase.GUIDToAssetPath(guids[0])}. Keep a single source of truth.");
        return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    public static List<T> FindAll<T>() where T : Object
    {
        var result = new List<T>();
        foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) result.Add(asset);
        }
        return result;
    }

    /// <summary>
    /// Puts the styled component this style's kind uses on the control, and applies the style — the one entry point
    /// every tool uses. Pass recordUndo false for prefab contents loaded off-scene, which the caller saves itself.
    /// </summary>
    public static StyledElement Attach(Component control, UIComponentStyle style, bool recordUndo = true)
    {
        IComponentKind kind = ComponentKinds.For(style)
                              ?? throw new System.ArgumentException($"No component kind uses {style.GetType().Name}.", nameof(style));
        GameObject go = control.gameObject;
        if (recordUndo) Undo.RegisterFullObjectHierarchyUndo(go, "Style control");
        var styled = (StyledElement)go.GetComponent(kind.StyledType);
        if (styled == null)
            styled = (StyledElement)(recordUndo ? Undo.AddComponent(go, kind.StyledType) : go.AddComponent(kind.StyledType));
        PrepareParts(styled, style, recordUndo);
        styled.AssignStyle(style);
        EnsureLayoutElement(styled, recordUndo);
        if (recordUndo) MarkDirty(styled);
        return styled;
    }

    /// <summary>The button form of <see cref="Attach(Selectable, UIComponentStyle, bool)"/>, typed for callers that need the StyledButton.</summary>
    public static StyledButton Attach(Selectable button, UIButtonStyle style, bool recordUndo = true) =>
        (StyledButton)Attach(button, (UIComponentStyle)style, recordUndo);

    /// <summary>
    /// Adds the Layout Element a style needs to size a button that a layout controls, then re-applies. Skipped on
    /// prefab instances, which get it from their prefab. Returns whether one was added.
    /// </summary>
    public static bool EnsureLayoutElement(StyledElement styled, bool recordUndo)
    {
        if (!styled.NeedsLayoutElement() || PrefabUtility.IsPartOfPrefabInstance(styled)) return false;
        if (recordUndo) Undo.AddComponent<LayoutElement>(styled.gameObject);
        else styled.gameObject.AddComponent<LayoutElement>();
        styled.Apply();
        return true;
    }

    /// <summary>Fills an existing styled control's part slots for its current style. Undoable.</summary>
    public static void Prepare(StyledElement styled)
    {
        Undo.RecordObject(styled, "Prepare styled control");
        PrepareParts(styled, styled.ComponentStyle, recordUndo: true);
        MarkDirty(styled);
    }

    // The label only gets a StyledText when the style drives typography: adding one re-fonts the label to the
    // role at once, which an unattended migration of a style with the role switched off must never do.
    private static void PrepareParts(StyledElement styled, UIComponentStyle style, bool recordUndo)
    {
        styled.AutoDetect();
        TMP_Text label = styled.Label;
        if (label == null || label.TryGetComponent(out StyledText _) || (style != null && !style.ApplyLabelRole)) return;

        StyledText text = recordUndo ? Undo.AddComponent<StyledText>(label.gameObject) : label.gameObject.AddComponent<StyledText>();
        text.Scale = TypographyAssets.Find();
        text.RoleId = style != null ? style.LabelRoleId : TextRole.Button.ToString();
    }

    /// <summary>Connects the theme to the project's type scale, which button labels take their fonts from.</summary>
    /// <summary>
    /// Gives a theme with no named sizes the three every project starts from: Regular at the theme's button height
    /// (56 when unset), Small 40 and XSmall 28. Each is edited in the theme afterwards.
    /// </summary>
    public static void EnsureThemeSizes(UITheme theme)
    {
        if (theme == null || theme.Sizes.Count > 0) return;
        Undo.RecordObject(theme, "Add named sizes");
        theme.GetOrAddSize("Regular", theme.ButtonHeight > 0f ? theme.ButtonHeight : 56f);
        theme.GetOrAddSize("Small", 40f);
        theme.GetOrAddSize("XSmall", 28f);
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssetIfDirty(theme);
    }

    public static void EnsureThemeTypography(UITheme theme)
    {
        if (theme == null || theme.Typography != null) return;
        TypographyScale scale = TypographyAssets.Find();
        if (scale == null) return;
        theme.Typography = scale;
        EditorUtility.SetDirty(theme);
    }

    /// <summary>Marks every object a styled control writes, so the edit is saved with its scene or prefab.</summary>
    public static void MarkDirty(StyledElement styled)
    {
        Dirty(styled);
        Dirty(styled.transform);
        Dirty(styled.GetComponent<Selectable>());
        Dirty(styled.GetComponent<LayoutGroup>());
        Dirty(styled.GetComponent<LayoutElement>());
        Dirty(styled.Background);
        if (styled.Label != null)
        {
            Dirty(styled.Label);
            Dirty(styled.Label.rectTransform);
            Dirty(styled.Label.GetComponent<StyledText>());
        }
        Dirty(styled.Icon);
        foreach (Object part in styled.WrittenParts()) Dirty(part);
        MarkSceneDirty(styled);
    }

    public static void MarkDirty(Component styled)
    {
        switch (styled)
        {
            case StyledElement e: MarkDirty(e); return;
            case StyledColor c:
                Dirty(c.GetComponent<Graphic>());
                break;
            case StyledText t:
                Dirty(t.GetComponent<TMP_Text>());
                break;
        }
        Dirty(styled);
        MarkSceneDirty(styled);
    }

    // Values written onto a prefab instance are left unrecorded: they preview the style in the scene while the
    // prefab asset stays the source (restyled by ReapplyProject), so instances never collect overrides that would
    // pin them to an old style. Edit mode and runtime both re-apply on load, so the preview never goes stale.
    private static void Dirty(Object o)
    {
        if (o != null && !PrefabUtility.IsPartOfPrefabInstance(o)) EditorUtility.SetDirty(o);
    }

    private static void MarkSceneDirty(Component c)
    {
        if (c != null && c.gameObject.scene.IsValid() && !EditorApplication.isPlaying && !PrefabUtility.IsPartOfPrefabInstance(c))
            EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
    }

    /// <summary>Applies one styled component and marks what it touched. Returns whether anything changed.</summary>
    public static bool ApplyOne(Component styled)
    {
        bool changed = Apply(styled);
        if (styled is StyledElement element) changed |= EnsureLayoutElement(element, recordUndo: true);
        if (changed) MarkDirty(styled);
        return changed;
    }

    private static bool Apply(Component styled) => styled switch
    {
        StyledElement e => e.Apply(),
        StyledColor c => c.Apply(),
        StyledText t => t.Apply(),
        _ => false,
    };

    private static readonly System.Type[] StyledTypes =
        { typeof(StyledElement), typeof(StyledColor), typeof(StyledText) };

    [MenuItem(DesignSystemMenu.Root + "Advanced/Re-apply Styles In Open Scenes", priority = 120)]
    public static void ReapplyOpen()
    {
        foreach (UITheme theme in FindAll<UITheme>()) EnsureThemeTypography(theme);
        VariantSync.SyncAll();
        int changed = 0;
        foreach (System.Type type in StyledTypes)
            foreach (Object o in Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (ApplyOne((Component)o)) changed++;

        // A prefab open in isolation lives in its own stage, which FindObjectsByType doesn't cover.
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null)
            foreach (System.Type type in StyledTypes)
                foreach (Component c in stage.prefabContentsRoot.GetComponentsInChildren(type, true))
                    if (ApplyOne(c)) changed++;

        if (changed > 0) Debug.Log($"[DesignSystem] Restyled {changed} component(s) in the open scene(s).");
        ButtonStatusView.Invalidate();
    }

    /// <summary>
    /// Re-applies every styled component in every prefab under Assets/ and saves only the prefabs that changed.
    /// Components that come from a nested prefab or a variant's base are skipped: their source prefab is visited
    /// in its own right, and writing them here would leave redundant overrides.
    /// </summary>
    [MenuItem(DesignSystemMenu.Root + "Advanced/Save Styles Into All Prefabs", priority = 121)]
    public static void ReapplyProject()
    {
        VariantSync.SyncAll();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int saved = 0;
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (EditorUtility.DisplayCancelableProgressBar("Applying styles", path, (float)i / guids.Length)) break;

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || !HasStyledComponents(asset)) continue;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = false;
                    foreach (System.Type type in StyledTypes)
                        foreach (Component c in root.GetComponentsInChildren(type, true))
                            if (!PrefabUtility.IsPartOfPrefabInstance(c))
                                changed |= Apply(c) | (c is StyledButton b && EnsureLayoutElement(b, recordUndo: false));
                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        saved++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        Debug.Log($"[DesignSystem] Restyled and saved {saved} prefab(s).");
        ReapplyOpen();
    }

    private static bool HasStyledComponents(GameObject asset)
    {
        foreach (System.Type type in StyledTypes)
            if (asset.GetComponentInChildren(type, true) != null) return true;
        return false;
    }
}
