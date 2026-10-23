using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generates (or refreshes) <c>TypographyPreview.prefab</c> next to the
/// <see cref="TypographyScale"/> asset: a canvas with one sample line per
/// <see cref="TextRole"/>, each driven by a <see cref="StyledText"/>. The designer
/// opens it and tweaks the scale asset to see every role render live.
///
/// Built in code (not hand-authored) so the TMP components are always valid, and so
/// it can be re-run to pick up new/renamed roles.
/// </summary>
public static class TypographyPreviewBuilder
{
    // Next to the type scale, so a project's typography assets stay together.
    private static string PreviewPathFor(TypographyScale scale)
    {
        string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(scale))?.Replace('\\', '/');
        return $"{(string.IsNullOrEmpty(folder) ? DesignSystemNames.DefaultAssetFolder : folder)}/TypographyPreview.prefab";
    }
    private const string Sample = "Sphinx of black quartz, judge my vow  1234567890";

    [MenuItem(DesignSystemMenu.TypographyRoot + "Create or Refresh Preview Prefab")]
    public static void Build()
    {
        TypographyScale scale = TypographyAssets.Find();
        if (scale == null)
        {
            EditorUtility.DisplayDialog("Typography",
                $"No TypographyScale asset found. Create one first (Assets ▸ Create ▸ {DesignSystemNames.TypographyAssetMenu.Replace("/", " ▸ ")}).",
                "OK");
            return;
        }

        // ---- Root canvas ----
        var root = new GameObject("TypographyPreview",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        // Constant pixel size = the literal point sizes, so roles read at their true size.
        root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // ---- Background (light, so dark text is readable) ----
        var bg = new GameObject("Background", typeof(Image));
        bg.transform.SetParent(root.transform, false);
        Stretch(bg.GetComponent<RectTransform>());
        bg.GetComponent<Image>().color = new Color(0.96f, 0.96f, 0.97f, 1f);

        // ---- Content column (top-anchored, auto-heights) ----
        var content = new GameObject("Content",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(root.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(-80f, 0f); // 40px side margins

        var vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(24, 24, 24, 24);
        vlg.spacing = 14f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ---- Header (fixed, not role-driven) ----
        AddPlainLine(content.transform, "TYPOGRAPHY PREVIEW",
            18f, FontStyles.Bold, new Color(0.35f, 0.35f, 0.4f, 1f));
        AddPlainLine(content.transform, "Edit TypographyScale.asset — every role updates here live.",
            13f, FontStyles.Normal, new Color(0.5f, 0.5f, 0.55f, 1f));

        // ---- One styled sample per role ----
        foreach (string role in scale.RoleIds)
        {
            var go = new GameObject(role,
                typeof(RectTransform), typeof(TextMeshProUGUI), typeof(StyledText));
            go.transform.SetParent(content.transform, false);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = $"{role}   {Sample}";
            tmp.color = new Color(0.11f, 0.11f, 0.13f, 1f);
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;

            var st = go.GetComponent<StyledText>();
            st.Scale = scale;
            st.RoleId = role; // applies font + size from the scale
        }

        // ---- Save + select ----
        string previewPath = PreviewPathFor(scale);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, previewPath);
        UnityEngine.Object.DestroyImmediate(root);

        AssetDatabase.Refresh();
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[Typography] Preview prefab written to {previewPath}. Double-click it to open, " +
                  "then edit TypographyScale.asset to see roles update.");
    }

    private static void AddPlainLine(Transform parent, string text, float size, FontStyles style, Color color)
    {
        var go = new GameObject("_" + text, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.enableWordWrapping = false;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
