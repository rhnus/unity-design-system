using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Project-wide settings for the design-system tools: what setup and Style Review scan, how close a
/// look must be to match a style, and how new buttons are styled. One asset, shared through version control.
/// </summary>
public class DesignSystemSettings : ScriptableObject
{
    public enum SceneScope
    {
        [InspectorName("Open scenes only")] OpenScenes,
        [InspectorName("Scenes in Build Settings")] BuildScenes,
        [InspectorName("Every scene in the project")] AllScenes,
    }

    private const string AssetPath = DesignSystemNames.DefaultAssetFolder + "/DesignSystemSettings.asset";

    [Header("Scan scope")]
    [Tooltip("Scenes are opened, scanned and closed in the background. Build Scenes covers what ships.")]
    public SceneScope sceneScope = SceneScope.BuildScenes;
    public bool scanPrefabs = true;
    [Tooltip("Only scan prefabs and scenes under these folders, e.g. Assets/UI/Menus. Empty = all of Assets. " +
             "Lets a large project adopt the design system one area at a time.")]
    public List<string> includedPaths = new List<string>();
    [Tooltip("Prefab and scene paths containing any of these are skipped — third-party packs, samples, sandboxes.")]
    public List<string> excludedPaths = new List<string> { "TextMesh Pro", "Plugins", "Samples", "ThirdParty" };

    [Header("Project")]
    [Tooltip("Title of the generated design system document. Empty = \"<project> Design System\".")]
    public string documentTitle = "";
    [Tooltip("Folder, relative to the project, for DESIGN_SYSTEM.md, tokens.json and the Figma snapshot.")]
    public string documentFolder = "docs/design-system";
    [Tooltip("Components whose class derives from a class with this name are drag cards. Empty = no drag cards.")]
    public string dragCardBaseType = "DraggableItem";

    [Header("Matching")]
    [Tooltip("Largest colour distance (0–1.7, RGBA) at which a button counts as the same look as a style. " +
             "0.12 merges near-duplicate shades without merging different colours.")]
    [Range(0.02f, 0.3f)] public float matchTolerance = 0.12f;
    [Tooltip("Setup only creates a style for a look shared by at least this many buttons or toggles; rarer looks go to Style Review.")]
    [Min(1)] public int minButtonsPerGeneratedStyle = 3;
    [Tooltip("Generated styles for sprites with colour baked in use the Recolor fill mode, so their colour becomes a " +
             "token a designer can change without new art. Off = Tint, which keeps the baked colour fixed.")]
    public bool recolorBakedSprites = true;

    [Header("Builds")]
    [Tooltip("Run the design check on styles and prefabs before every build. Interactive builds ask before continuing " +
             "when it finds problems; batch builds only log them.")]
    public bool checkBeforeBuild = true;

    [Header("New buttons & toggles")]
    [Tooltip("Give every Button or Toggle created in the editor the default style of its kind.")]
    public bool autoStyleNewButtons = true;
    public UIButtonStyle defaultButtonStyle;
    [Tooltip("Setup picks the most-used toggle style. A new toggle built differently — a checkbox for a card style — is left alone.")]
    public UIToggleStyle defaultToggleStyle;

    [Header("Drag cards")]
    [Tooltip("The drag card every other card family converges on. Set from a drag card style's ⋯ menu on the Styles tab.")]
    public UIDragCardStyle standardDragCard;

    [Header("Text fit")]
    [Tooltip("Content text and the controls it appears in. Check measures every string at its control's label width and " +
             "reports text that overflows a fixed-height control or runs past the line limit.")]
    public List<TextFitRule> textFitRules = new List<TextFitRule>();

    /// <summary>The style new controls of this kind get, or null for none.</summary>
    public UIComponentStyle DefaultStyleFor(IComponentKind kind) =>
        kind == ComponentKinds.Buttons ? defaultButtonStyle : kind == ComponentKinds.Toggles ? defaultToggleStyle : (UIComponentStyle)null;

    /// <summary>The settings asset, or null when none exists yet. Never creates one.</summary>
    public static DesignSystemSettings Find() => DesignSystemAssets.FindSingle<DesignSystemSettings>();

    private static string cachedDragCardBase;

    /// <summary>The drag card base class name, read once: kind detection asks for it for every component it scans.</summary>
    public static string DragCardBaseType => cachedDragCardBase ??= Find() is DesignSystemSettings s && s.dragCardBaseType != null
        ? s.dragCardBaseType.Trim()
        : "DraggableItem";

    public string DocumentTitle => string.IsNullOrWhiteSpace(documentTitle) ? $"{PlayerSettings.productName} Design System" : documentTitle.Trim();

    public string DocumentFolder => string.IsNullOrWhiteSpace(documentFolder) ? "docs/design-system" : documentFolder.Trim().TrimEnd('/');

    private void OnValidate() => cachedDragCardBase = null;

    public static DesignSystemSettings GetOrCreate()
    {
        DesignSystemSettings settings = Find();
        if (settings != null) return settings;

        Directory.CreateDirectory(DesignSystemNames.DefaultAssetFolder);
        settings = CreateInstance<DesignSystemSettings>();
        AssetDatabase.CreateAsset(settings, AssetPath);
        AssetDatabase.SaveAssets();
        DesignSystemAssets.ForgetFolder();
        return settings;
    }

    /// <summary>Whether a prefab or scene path is out of scope: outside every included folder, or excluded.</summary>
    /// <summary>Whether the path is in a third-party folder from Excluded Paths, whatever the included area.</summary>
    public bool IsThirdParty(string path) =>
        !string.IsNullOrEmpty(path) && excludedPaths.Any(x => !string.IsNullOrWhiteSpace(x) && path.Contains(x.Trim()));

    public bool IsExcluded(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        List<string> included = includedPaths.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().TrimEnd('/')).ToList();
        if (included.Count > 0 && !included.Any(x => path.StartsWith(x + "/") || path == x)) return true;
        return excludedPaths.Any(x => !string.IsNullOrWhiteSpace(x) && path.Contains(x.Trim()));
    }

    /// <summary>Scene paths in scope. Open scenes include untitled ones, which have an empty path.</summary>
    public List<string> ScenePaths()
    {
        IEnumerable<string> paths = sceneScope switch
        {
            SceneScope.BuildScenes => EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path),
            SceneScope.AllScenes => AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath),
            _ => Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Where(s => s.isLoaded).Select(s => s.path),
        };
        return paths.Where(p => sceneScope == SceneScope.OpenScenes || !IsExcluded(p)).Distinct().ToList();
    }

    [MenuItem(DesignSystemMenu.Root + "Advanced/Settings", priority = 160)]
    private static void Select()
    {
        Selection.activeObject = GetOrCreate();
        EditorGUIUtility.PingObject(Selection.activeObject);
    }
}
