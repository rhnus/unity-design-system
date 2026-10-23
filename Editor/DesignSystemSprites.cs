using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps every sprite token's recolour material in step with its sprite, so <see cref="SurfaceFillMode.Recolor"/>
/// works on existing art without anyone touching a material. One small material per sprite, next to the theme.
/// </summary>
public static class RecolorMaterials
{
    private static readonly int RefLuminance = Shader.PropertyToID("_RefLuminance");

    /// <summary>Creates or refreshes the recolour material of every sprite token. Returns how many changed.</summary>
    public static int Ensure(UITheme theme)
    {
        string themePath = AssetDatabase.GetAssetPath(theme);
        if (string.IsNullOrEmpty(themePath)) return 0;
        Shader shader = Shader.Find(SurfaceLook.RecolorShader);
        if (shader == null)
        {
            Debug.LogError($"[DesignSystem] Shader '{SurfaceLook.RecolorShader}' is missing — Recolor fill mode will fall back to Tint.");
            return 0;
        }

        string folder = Path.GetDirectoryName(themePath).Replace('\\', '/') + "/Generated/Materials";
        int changed = 0;
        foreach (UITheme.SpriteToken token in theme.Sprites)
        {
            if (token == null || token.sprite == null) continue;
            if (token.recolorMaterial == null)
            {
                Directory.CreateDirectory(folder);
                var material = new Material(shader);
                AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{Safe(token.sprite.name)}_Recolor.mat"));
                Undo.RecordObject(theme, "Create recolour material");
                token.recolorMaterial = material;
                EditorUtility.SetDirty(theme);
                changed++;
            }

            float lum = BodyLuminance(token.sprite);
            if (!Mathf.Approximately(token.recolorMaterial.GetFloat(RefLuminance), lum))
            {
                token.recolorMaterial.SetFloat(RefLuminance, lum);
                EditorUtility.SetDirty(token.recolorMaterial);
                changed++;
            }
        }
        if (changed > 0) AssetDatabase.SaveAssets();
        return changed;
    }

    /// <summary>
    /// Luminance of the sprite's body colour (its centre) in the space the shader samples in: linear when the
    /// project renders in linear space, since sRGB textures are linearised on sampling.
    /// </summary>
    public static float BodyLuminance(Sprite sprite)
    {
        Color c = ButtonScanner.SpriteCentreColour(sprite);
        if (PlayerSettings.colorSpace == ColorSpace.Linear) c = c.linear;
        return Mathf.Max(0.001f, 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b);
    }

    /// <summary>Whether the sprite carries its own colour, so tinting it can only darken that colour.</summary>
    public static bool HasBakedColour(Sprite sprite)
    {
        if (sprite == null) return false;
        Color.RGBToHSV(ButtonScanner.SpriteCentreColour(sprite), out _, out float s, out float v);
        return s > 0.15f || v < 0.85f;
    }

    private static string Safe(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    [MenuItem(DesignSystemMenu.Root + "Advanced/Refresh Recolour Materials", priority = 122)]
    private static void RefreshAll()
    {
        int n = 0;
        foreach (UITheme theme in DesignSystemAssets.FindAll<UITheme>()) n += Ensure(theme);
        Debug.Log($"[DesignSystem] Refreshed {n} recolour material setting(s).");
        DesignSystemAssets.ReapplyOpen();
    }
}

/// <summary>Draws a <see cref="ThemeSprite"/> as a dropdown of the theme's sprite tokens plus a thumbnail.</summary>
[CustomPropertyDrawer(typeof(ThemeSprite))]
public class ThemeSpriteDrawer : PropertyDrawer
{
    private const float ThumbWidth = 36f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty idProp = property.FindPropertyRelative(nameof(ThemeSprite.tokenId));
        label = EditorGUI.BeginProperty(position, label, property);
        Rect field = EditorGUI.PrefixLabel(position, label);

        SerializedProperty themeProp = property.serializedObject.FindProperty("theme");
        UITheme theme = themeProp != null && themeProp.objectReferenceValue is UITheme own ? own : ThemeColorDrawer.ProjectTheme;
        if (theme == null)
        {
            EditorGUI.LabelField(field, "No UITheme");
            EditorGUI.EndProperty();
            return;
        }

        var tokens = theme.Sprites;
        string id = idProp.stringValue;
        bool missing = !string.IsNullOrEmpty(id) && theme.GetSprite(id) == null;
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

        var popupRect = new Rect(field.x, field.y, field.width - ThumbWidth - 4f, field.height);
        var thumbRect = new Rect(field.xMax - ThumbWidth, field.y, ThumbWidth, field.height);

        EditorGUI.showMixedValue = idProp.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        int picked = EditorGUI.Popup(popupRect, current, names);
        if (EditorGUI.EndChangeCheck() && !(missing && picked == names.Length - 1))
            idProp.stringValue = picked == 0 ? string.Empty : tokens[picked - 1].id;
        EditorGUI.showMixedValue = false;

        Sprite sprite = theme.GetSprite(idProp.stringValue)?.sprite;
        Texture preview = sprite != null ? AssetPreview.GetAssetPreview(sprite) : null;
        if (preview != null) GUI.DrawTexture(thumbRect, preview, ScaleMode.ScaleToFit);

        EditorGUI.EndProperty();
    }
}
