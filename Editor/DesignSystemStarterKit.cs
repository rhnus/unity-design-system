using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Creates the first theme and styles from the project's most-used button looks, and builds a preview prefab that
/// renders every style so a designer can tune them live. Never overwrites an asset that already exists.
/// </summary>
public static class DesignSystemStarterKit
{
    public static string PreviewPath => DesignSystemAssets.DefaultFolder + "/DesignSystemPreview.prefab";

    // Preferred white rounded-rect sprites from this project. Any project without them falls back to Unity's own
    // UISprite, so the kit works anywhere.
    private const string SemiRoundedGuid = "06839a9e285134212915dda98b0834fa";
    private const string SecondaryRectGuid = "f551f435bb534b04f849338f622b4ab8";
    private const string BuiltinRounded = "UI/Skin/UISprite.psd";

    [MenuItem(DesignSystemMenu.Root + "Advanced/Create Starter Kit (new projects)", priority = 140)]
    public static void Create()
    {
        Directory.CreateDirectory(DesignSystemAssets.DefaultFolder);

        UITheme theme = DesignSystemAssets.FindSingle<UITheme>();
        if (theme == null)
        {
            theme = ScriptableObject.CreateInstance<UITheme>();
            Seed(theme, "Brand/Primary", "#FF7D34");
            Seed(theme, "Brand/Accent", "#F3A808");
            Seed(theme, "Neutral/White", "#FFFFFF");
            Seed(theme, "Neutral/Surface", "#FCFDFE");
            Seed(theme, "Neutral/Transparent", "#FFFFFF00");
            Seed(theme, "Text/Default", "#081631");
            Seed(theme, "Text/Secondary", "#4C5261");
            Seed(theme, "Text/OnPrimary", "#FFFFFF");
            AssetDatabase.CreateAsset(theme, DesignSystemAssets.DefaultFolder + "/UITheme.asset");
        }

        Sprite builtin = AssetDatabase.GetBuiltinExtraResource<Sprite>(BuiltinRounded);
        Sprite semiRounded = LoadSprite(SemiRoundedGuid) ?? builtin;
        Sprite secondaryRect = LoadSprite(SecondaryRectGuid) ?? semiRounded;

        CreateButtonStyle(theme, "Button_Primary", semiRounded, 15f, "Brand/Primary", "Text/OnPrimary");
        CreateButtonStyle(theme, "Button_Secondary", secondaryRect, 15f, "Neutral/White", "Brand/Primary");
        CreateButtonStyle(theme, "Button_Ghost", semiRounded, 15f, "Neutral/Transparent", "Brand/Primary");
        CreateSurfaceStyle(theme, "Surface_Card", semiRounded, 15f, "Neutral/Surface");

        DesignSystemSettings settings = DesignSystemSettings.GetOrCreate();
        if (settings.defaultButtonStyle == null)
        {
            settings.defaultButtonStyle = AssetDatabase.LoadAssetAtPath<UIButtonStyle>(DesignSystemAssets.DefaultFolder + "/Button_Primary.asset");
            EditorUtility.SetDirty(settings);
        }

        EditorUtility.SetDirty(theme);
        RecolorMaterials.Ensure(theme);
        AssetDatabase.SaveAssets();
        BuildPreview();
        Selection.activeObject = theme;
        Debug.Log($"[DesignSystem] Starter kit ready in {DesignSystemAssets.DefaultFolder}. Next: open the Design System window to style your buttons.");
    }

    private static void Seed(UITheme theme, string name, string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        theme.SetColor(name, c);
    }

    private static Sprite LoadSprite(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static string Token(UITheme theme, string name) => theme.FindByName(name)?.id ?? string.Empty;

    private static void CreateButtonStyle(UITheme theme, string name, Sprite sprite, float radius, string fill, string label)
    {
        string path = $"{DesignSystemAssets.DefaultFolder}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<UIButtonStyle>(path) != null) return;

        var style = ScriptableObject.CreateInstance<UIButtonStyle>();
        AssetDatabase.CreateAsset(style, path);
        var so = new SerializedObject(style);
        so.FindProperty("theme").objectReferenceValue = theme;
        WriteLook(so.FindProperty("background"), theme, sprite, radius, Token(theme, fill));
        so.FindProperty("labelColor.tokenId").stringValue = Token(theme, label);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateSurfaceStyle(UITheme theme, string name, Sprite sprite, float radius, string fill)
    {
        string path = $"{DesignSystemAssets.DefaultFolder}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<UISurfaceStyle>(path) != null) return;

        var style = ScriptableObject.CreateInstance<UISurfaceStyle>();
        AssetDatabase.CreateAsset(style, path);
        var so = new SerializedObject(style);
        so.FindProperty("theme").objectReferenceValue = theme;
        WriteLook(so.FindProperty("background"), theme, sprite, radius, Token(theme, fill));
        SerializedProperty padding = so.FindProperty("padding");
        foreach (string side in new[] { "m_Left", "m_Right", "m_Top", "m_Bottom" })
            padding.FindPropertyRelative(side).intValue = 16;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WriteLook(SerializedProperty look, UITheme theme, Sprite sprite, float radius, string fillId)
    {
        look.FindPropertyRelative(nameof(SurfaceLook.sprite)).objectReferenceValue = sprite;
        if (sprite != null)
            look.FindPropertyRelative("shape.tokenId").stringValue = theme.GetOrAddSprite($"Shape/{sprite.name}", sprite).id;
        look.FindPropertyRelative(nameof(SurfaceLook.cornerRadius)).floatValue = radius;
        look.FindPropertyRelative("fill.tokenId").stringValue = fillId;
    }

    /// <summary>
    /// Builds a canvas with one sample per Button and Surface style in the project. Open it, select a style asset,
    /// and every edit re-renders here — the style editors push changes into the open prefab stage.
    /// </summary>
    [MenuItem(DesignSystemMenu.Root + "Advanced/Create or Refresh Preview Prefab", priority = 141)]
    public static void BuildPreview()
    {
        Directory.CreateDirectory(DesignSystemAssets.DefaultFolder);
        var root = new GameObject("DesignSystemPreview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        var column = new GameObject("Samples", typeof(RectTransform), typeof(VerticalLayoutGroup));
        column.transform.SetParent(root.transform, false);
        var columnRect = (RectTransform)column.transform;
        columnRect.anchorMin = Vector2.zero;
        columnRect.anchorMax = Vector2.one;
        columnRect.offsetMin = columnRect.offsetMax = Vector2.zero;
        var layout = column.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(40, 40, 40, 40);
        layout.spacing = 24f;
        layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        foreach (UIButtonStyle style in DesignSystemAssets.FindAll<UIButtonStyle>())
            AddButton(column.transform, style);
        foreach (UISurfaceStyle style in DesignSystemAssets.FindAll<UISurfaceStyle>())
            AddSurface(column.transform, style);

        PrefabUtility.SaveAsPrefabAsset(root, PreviewPath);
        Object.DestroyImmediate(root);
    }

    private static void AddButton(Transform parent, UIButtonStyle style)
    {
        var go = new GameObject(style.name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = new Vector2(240f, style.Height > 0f ? style.Height : 56f);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = style.name.Replace("Button_", "");
        label.alignment = TextAlignmentOptions.Center;

        DesignSystemAssets.Attach(go.GetComponent<Button>(), style, recordUndo: false);
    }

    private static void AddSurface(Transform parent, UISurfaceStyle style)
    {
        var go = new GameObject(style.name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = new Vector2(360f, 160f);
        go.AddComponent<StyledSurface>().Style = style;
    }
}
