using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// A starter design system as data: colours, named sizes, a type scale and the core styles. The package ships its
/// presets in its Starter Kit sample; a project can add its own anywhere under Assets as a <c>.preset.json</c> file.
/// </summary>
public class StarterPreset
{
    public const string Kind = "design-system-preset";

    public string kind;
    public string name;
    public string description;
    public Dictionary<string, string> colors = new Dictionary<string, string>();
    public List<SizeSpec> sizes = new List<SizeSpec>();
    public string buttonLabelRole = "Button";
    public float cornerRadius = 12f;
    public Dictionary<string, RoleSpec> typography = new Dictionary<string, RoleSpec>();
    public List<StyleSpec> buttons = new List<StyleSpec>();
    public List<StyleSpec> surfaces = new List<StyleSpec>();
    public AnswerSpec answers;

    /// <summary>Where the preset was read from, shown in the wizard.</summary>
    [JsonIgnore] public string Source;
    [JsonIgnore] public bool FromPackage;

    public class SizeSpec { public string name; public float height; public string labelRole; }
    public class RoleSpec { public float size; public string style; }
    public class StyleSpec { public string name, fill, label, size, variantOf; public int padding; }
    public class AnswerSpec { public string right, wrong; }
}

/// <summary>
/// Starts a new project's design system from a <see cref="StarterPreset"/>: theme, type scale, a rounded shape
/// sprite, button and panel styles, settings and a preview prefab, all as the project's own assets in one folder.
/// Never overwrites an asset and never writes into the package.
/// </summary>
public static class DesignSystemStarterKit
{
    public class Options
    {
        public string folder = DesignSystemNames.DefaultAssetFolder;
        /// <summary>Replaces the preset's Brand/Primary.</summary>
        public Color? brand;
        /// <summary>The font every role uses; null keeps each text's own font.</summary>
        public TMP_FontAsset font;
        public string documentTitle;
        public bool buildPreview = true;
    }

    public const string BrandToken = "Brand/Primary";
    public const string ShapeToken = "Shape/Rounded";
    private const string PresetSuffix = ".preset.json";
    private const string SampleFolder = "Samples~/Starter Kit";
    private const int ShapeSize = 64;
    private const int ShapeRadius = 16;

    public static string PreviewPath => DesignSystemAssets.DefaultFolder + "/DesignSystemPreview.prefab";

    /// <summary>Why this project can't start from a preset, or null when it can: the kit is for projects with no theme yet.</summary>
    public static string WhyNotStart()
    {
        string[] themes = AssetDatabase.FindAssets("t:" + nameof(UITheme));
        return themes.Length == 0 ? null
            : $"This project already has a design system ({AssetDatabase.GUIDToAssetPath(themes[0])}). The starter kit is for " +
              "projects without one; to style existing UI, use Scan on the Design System window.";
    }

    // ---- Presets ----

    /// <summary>The package's presets, then the project's own.</summary>
    public static List<StarterPreset> Presets()
    {
        var presets = new List<StarterPreset>();
        string package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DesignSystemStarterKit).Assembly)?.resolvedPath;
        string folder = package == null ? null : Path.Combine(package, SampleFolder);
        if (folder != null && Directory.Exists(folder))
            foreach (string file in Directory.GetFiles(folder, "*" + PresetSuffix).OrderBy(f => f))
                if (Parse(File.ReadAllText(file), $"{DesignSystemNames.PackageName}/{SampleFolder}/{Path.GetFileName(file)}") is StarterPreset p)
                {
                    p.FromPackage = true;
                    presets.Add(p);
                }

        foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(PresetSuffix, StringComparison.OrdinalIgnoreCase) && Parse(File.ReadAllText(path), path) is StarterPreset p)
                presets.Add(p);
        }
        return presets;
    }

    /// <summary>The preset in this JSON, or null when it isn't one.</summary>
    public static StarterPreset Parse(string json, string source)
    {
        try
        {
            var preset = JsonConvert.DeserializeObject<StarterPreset>(json);
            if (preset == null || preset.kind != StarterPreset.Kind) return null;
            preset.Source = source;
            return preset;
        }
        catch (JsonException e)
        {
            Debug.LogWarning($"[DesignSystem] {source} isn't a readable preset: {e.Message}");
            return null;
        }
    }

    /// <summary>What in the preset doesn't add up — a colour, size or parent style it names but doesn't define.</summary>
    public static List<string> Problems(StarterPreset preset)
    {
        var problems = new List<string>();
        if (preset.sizes.Count == 0) problems.Add("It defines no sizes.");
        var sizeNames = new HashSet<string>(preset.sizes.Select(s => s.name));
        var buttonNames = new HashSet<string>();
        foreach (StarterPreset.StyleSpec s in preset.buttons.Concat(preset.surfaces))
        {
            if (string.IsNullOrWhiteSpace(s.name)) { problems.Add("A style has no name."); continue; }
            if (!buttonNames.Add(s.name)) problems.Add($"{s.name} is defined twice.");
            foreach (string colour in new[] { s.fill, s.label }.Where(c => !string.IsNullOrEmpty(c)))
                if (!preset.colors.ContainsKey(colour)) problems.Add($"{s.name} uses colour {colour}, which isn't in colors.");
            if (!string.IsNullOrEmpty(s.size) && !sizeNames.Contains(s.size)) problems.Add($"{s.name} uses size {s.size}, which isn't in sizes.");
            if (!string.IsNullOrEmpty(s.variantOf) && !preset.buttons.Any(b => b.name == s.variantOf && string.IsNullOrEmpty(b.variantOf)))
                problems.Add($"{s.name} is a variant of {s.variantOf}, which isn't a button style without a parent.");
        }
        foreach (string colour in new[] { preset.answers?.right, preset.answers?.wrong }.Where(c => !string.IsNullOrEmpty(c)))
            if (!preset.colors.ContainsKey(colour)) problems.Add($"answers uses colour {colour}, which isn't in colors.");
        foreach (var c in preset.colors)
            if (!ColorUtility.TryParseHtmlString(c.Value, out _)) problems.Add($"Colour {c.Key} = {c.Value} isn't a #RRGGBB value.");
        return problems;
    }

    // ---- Creating ----

    /// <summary>Every asset <see cref="Create"/> writes, so a caller can see what it will make before it does.</summary>
    public static List<string> PlannedPaths(StarterPreset preset, string folder)
    {
        folder = folder.TrimEnd('/');
        var paths = new List<string> { $"{folder}/UITheme.asset", $"{folder}/TypographyScale.asset", $"{folder}/Shapes/Rounded.png" };
        paths.AddRange(preset.buttons.Concat(preset.surfaces).Select(s => $"{folder}/{s.name}.asset"));
        if (DesignSystemSettings.Find() == null) paths.Add($"{folder}/DesignSystemSettings.asset");
        return paths;
    }

    /// <summary>Builds the preset's assets in the folder and returns their paths. Refuses when any of them exists already.</summary>
    public static List<string> Create(StarterPreset preset, Options options)
    {
        List<string> problems = Problems(preset);
        if (problems.Count > 0) throw new ArgumentException($"Preset {preset.name}: {string.Join(" ", problems)}");
        string folder = options.folder.TrimEnd('/');
        List<string> paths = PlannedPaths(preset, folder);
        string taken = paths.FirstOrDefault(File.Exists);
        if (taken != null) throw new InvalidOperationException($"{taken} already exists, and the starter kit never overwrites. Pick another folder.");

        Directory.CreateDirectory(folder);
        Sprite shape = WriteShape($"{folder}/Shapes/Rounded.png");
        TypographyScale scale = CreateScale(preset, options.font, $"{folder}/TypographyScale.asset");
        UITheme theme = CreateTheme(preset, options.brand, scale, shape, $"{folder}/UITheme.asset");

        var buttons = new Dictionary<string, UIButtonStyle>();
        foreach (StarterPreset.StyleSpec spec in preset.buttons.Where(b => string.IsNullOrEmpty(b.variantOf)))
            buttons[spec.name] = CreateButton(theme, preset, spec, $"{folder}/{spec.name}.asset");
        foreach (StarterPreset.StyleSpec spec in preset.buttons.Where(b => !string.IsNullOrEmpty(b.variantOf)))
            buttons[spec.name] = CreateVariant(theme, buttons[spec.variantOf], spec, $"{folder}/{spec.name}.asset");
        var surfaces = preset.surfaces.Select(spec => CreateSurface(theme, preset, spec, $"{folder}/{spec.name}.asset")).ToList();

        WriteSettings(folder, options.documentTitle, buttons.Values.FirstOrDefault());
        RecolorMaterials.Ensure(theme);
        AssetDatabase.SaveAssets();

        if (options.buildPreview)
        {
            string preview = $"{folder}/DesignSystemPreview.prefab";
            BuildPreview(preview, buttons.Values.Cast<UIComponentStyle>().Concat(surfaces));
            paths.Add(preview);
        }
        return paths;
    }

    private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;

    private static TypographyScale CreateScale(StarterPreset preset, TMP_FontAsset font, string path)
    {
        var scale = ScriptableObject.CreateInstance<TypographyScale>();
        foreach (var entry in preset.typography)
        {
            TypographyScale.RoleStyle role = scale.AddRole(entry.Key);
            if (TextRoles.TryGetBuiltIn(entry.Key, out TextRole builtIn))
            {
                role.role = builtIn;
                role.id = "";
            }
            role.font = font;
            role.fontSize = entry.Value.size;
            role.fontStyle = Enum.TryParse(entry.Value.style ?? "", true, out FontStyles style) ? style : FontStyles.Normal;
        }
        // Built-in roles the preset leaves out still exist, so code that names them always finds one.
        foreach (TextRole builtIn in Enum.GetValues(typeof(TextRole)))
            if (scale.Get(builtIn) == null)
            {
                TypographyScale.RoleStyle role = scale.AddRole(builtIn.ToString(), copyFrom: TextRole.Body.ToString());
                role.role = builtIn;
                role.id = "";
                role.font = font;
            }
        AssetDatabase.CreateAsset(scale, path);
        return scale;
    }

    private static UITheme CreateTheme(StarterPreset preset, Color? brand, TypographyScale scale, Sprite shape, string path)
    {
        var theme = ScriptableObject.CreateInstance<UITheme>();
        foreach (var colour in preset.colors) theme.SetColor(colour.Key, Hex(colour.Value));
        if (brand.HasValue) theme.SetColor(BrandToken, brand.Value);

        foreach (StarterPreset.SizeSpec spec in preset.sizes)
        {
            UITheme.SizeToken size = theme.GetOrAddSize(spec.name, spec.height);
            size.setLabelRole = !string.IsNullOrEmpty(spec.labelRole);
            size.labelRoleId = spec.labelRole ?? "";
        }
        theme.ButtonHeight = preset.sizes[0].height;
        theme.SetButtonLabelFont = true;
        theme.ButtonLabelRoleId = preset.buttonLabelRole;
        theme.Typography = scale;
        theme.GetOrAddSprite(ShapeToken, shape);

        if (preset.answers != null)
        {
            theme.RightFill = ThemeColor.From(theme.FindByName(preset.answers.right));
            theme.WrongFill = ThemeColor.From(theme.FindByName(preset.answers.wrong));
        }
        AssetDatabase.CreateAsset(theme, path);
        return theme;
    }

    private static void PaintLook(SurfaceLook look, UITheme theme, float radius, string fill)
    {
        UITheme.SpriteToken shape = theme.Sprites.First(s => s.name == ShapeToken);
        look.shape = ThemeSprite.From(shape);
        look.sprite = shape.sprite;
        look.cornerRadius = radius;
        look.fill = ThemeColor.From(theme.FindByName(fill));
    }

    private static UITheme.SizeToken SizeFor(UITheme theme, StarterPreset preset, string name) =>
        theme.Sizes.First(s => s.name == (string.IsNullOrEmpty(name) ? preset.sizes[0].name : name));

    private static UIButtonStyle CreateButton(UITheme theme, StarterPreset preset, StarterPreset.StyleSpec spec, string path)
    {
        var style = ScriptableObject.CreateInstance<UIButtonStyle>();
        style.Theme = theme;
        PaintLook(style.Background, theme, preset.cornerRadius, spec.fill);
        style.LabelColor = ThemeColor.From(theme.FindByName(spec.label));
        style.LabelFontSource = LabelFontSource.Theme;
        style.HeightSource = ButtonHeightSource.NamedSize;
        style.Size = ThemeSize.From(SizeFor(theme, preset, spec.size));
        AssetDatabase.CreateAsset(style, path);
        return style;
    }

    // A variant follows its parent in everything but its size, so changing the parent's fill restyles both.
    private static UIButtonStyle CreateVariant(UITheme theme, UIButtonStyle parent, StarterPreset.StyleSpec spec, string path)
    {
        var style = ScriptableObject.CreateInstance<UIButtonStyle>();
        AssetDatabase.CreateAsset(style, path);
        style.BaseStyle = parent;
        style.Overrides.AddRange(new[] { "heightSource", "height", "size" });
        VariantSync.Sync(style);
        style.HeightSource = ButtonHeightSource.NamedSize;
        style.Size = ThemeSize.From(theme.Sizes.First(s => s.name == spec.size));
        EditorUtility.SetDirty(style);
        return style;
    }

    private static UISurfaceStyle CreateSurface(UITheme theme, StarterPreset preset, StarterPreset.StyleSpec spec, string path)
    {
        var style = ScriptableObject.CreateInstance<UISurfaceStyle>();
        style.Theme = theme;
        PaintLook(style.Background, theme, preset.cornerRadius, spec.fill);
        AssetDatabase.CreateAsset(style, path);
        var so = new SerializedObject(style);
        so.FindProperty("applyLayout").boolValue = spec.padding > 0;
        SerializedProperty padding = so.FindProperty("padding");
        foreach (string side in new[] { "m_Left", "m_Right", "m_Top", "m_Bottom" })
            padding.FindPropertyRelative(side).intValue = spec.padding;
        so.ApplyModifiedPropertiesWithoutUndo();
        return style;
    }

    // A project that already has settings keeps them; only a missing default button style is filled in.
    private static void WriteSettings(string folder, string documentTitle, UIButtonStyle defaultButton)
    {
        DesignSystemSettings settings = DesignSystemSettings.Find();
        if (settings == null)
        {
            settings = DesignSystemSettings.GetOrCreate(folder);
            settings.documentTitle = documentTitle ?? "";
            settings.dragCardBaseType = "";
        }
        if (settings.defaultButtonStyle == null) settings.defaultButtonStyle = defaultButton;
        EditorUtility.SetDirty(settings);
        DesignSystemSettings.ForgetCached();
    }

    /// <summary>A white rounded rectangle, sliced at its corners, that every starter style draws and tints.</summary>
    private static Sprite WriteShape(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var texture = new Texture2D(ShapeSize, ShapeSize, TextureFormat.RGBA32, false);
        var pixels = new Color32[ShapeSize * ShapeSize];
        for (int y = 0; y < ShapeSize; y++)
            for (int x = 0; x < ShapeSize; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var centre = new Vector2(Mathf.Clamp(p.x, ShapeRadius, ShapeSize - ShapeRadius), Mathf.Clamp(p.y, ShapeRadius, ShapeSize - ShapeRadius));
                float alpha = Mathf.Clamp01(ShapeRadius - Vector2.Distance(p, centre) + 0.5f);
                pixels[y * ShapeSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        texture.SetPixels32(pixels);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = new Vector4(ShapeRadius, ShapeRadius, ShapeRadius, ShapeRadius);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>The project's default TMP font, or null when TextMesh Pro's essentials aren't imported yet.</summary>
    public static TMP_FontAsset DefaultFont() =>
        Resources.Load<TMP_Settings>("TMP Settings") != null ? TMP_Settings.defaultFontAsset : null;

    // ---- Preview ----

    /// <summary>
    /// Builds a canvas with one sample per Button and Surface style in the project. Open it, select a style asset,
    /// and every edit re-renders here — the style editors push changes into the open prefab stage.
    /// </summary>
    [MenuItem(DesignSystemMenu.Root + "Advanced/Create or Refresh Preview Prefab", priority = 141)]
    public static void BuildPreview() =>
        BuildPreview(PreviewPath, DesignSystemAssets.FindAll<UIButtonStyle>().Cast<UIComponentStyle>().Concat(DesignSystemAssets.FindAll<UISurfaceStyle>()));

    public static void BuildPreview(string path, IEnumerable<UIComponentStyle> styles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
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

        foreach (UIComponentStyle style in styles)
            switch (style)
            {
                case UIButtonStyle button: AddButton(column.transform, button); break;
                case UISurfaceStyle surface: AddSurface(column.transform, surface); break;
            }

        PrefabUtility.SaveAsPrefabAsset(root, path);
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
        label.text = style.name.Replace("Button_", "").Replace('_', ' ');
        label.alignment = TextAlignmentOptions.Center;

        DesignSystemAssets.Attach(go.GetComponent<Button>(), style, recordUndo: false);
        if (labelGo.TryGetComponent(out StyledText text) && style.Theme != null && style.Theme.Typography != null)
            text.Scale = style.Theme.Typography;
    }

    private static void AddSurface(Transform parent, UISurfaceStyle style)
    {
        var go = new GameObject(style.name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = new Vector2(360f, 160f);
        go.AddComponent<StyledSurface>().Style = style;
    }
}
