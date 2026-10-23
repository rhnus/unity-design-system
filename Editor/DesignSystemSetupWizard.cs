using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Starts a new project's design system from a preset: pick the preset, a brand colour, a font and a folder, and
/// <see cref="DesignSystemStarterKit.Create"/> builds the project's own assets there.
/// </summary>
public class DesignSystemSetupWizard : EditorWindow
{
    [MenuItem(DesignSystemMenu.Root + "Set Up New Project…", priority = 1)]
    public static void Open()
    {
        var window = GetWindow<DesignSystemSetupWizard>(true, "Set Up Design System");
        window.minSize = new Vector2(460f, 420f);
    }

    private List<StarterPreset> presets = new List<StarterPreset>();
    private int index;
    private Color brand = Color.white;
    private TMP_FontAsset font;
    private string folder = DesignSystemNames.DefaultAssetFolder;
    private string documentTitle;
    private string blocked;
    private Vector2 scroll;
    private GUIStyle wrap;

    private StarterPreset Selected => index >= 0 && index < presets.Count ? presets[index] : null;

    private void OnEnable()
    {
        font = DesignSystemStarterKit.DefaultFont();
        documentTitle = $"{PlayerSettings.productName} Design System";
        Reload();
    }

    // A preset file edited or added while the window is open shows up when the window is next focused.
    private void OnFocus() => Reload();

    private void Reload()
    {
        string current = Selected?.Source;
        presets = DesignSystemStarterKit.Presets();
        int keep = presets.FindIndex(p => p.Source == current);
        int neutral = presets.FindIndex(p => p.FromPackage && p.name == "Neutral");
        int next = keep >= 0 ? keep : Math.Max(0, neutral);
        if (current == null || keep < 0) Select(next);
        else index = next;
        blocked = DesignSystemStarterKit.WhyNotStart();
    }

    private void Select(int i)
    {
        index = i;
        if (Selected != null && Selected.colors.TryGetValue(DesignSystemStarterKit.BrandToken, out string hex))
            ColorUtility.TryParseHtmlString(hex, out brand);
    }

    private void OnGUI()
    {
        wrap ??= new GUIStyle(EditorStyles.label) { wordWrap = true };
        if (blocked != null)
        {
            EditorGUILayout.HelpBox(blocked, MessageType.Info);
            if (GUILayout.Button("Open Design System")) DesignSystemHub.Open();
            return;
        }
        if (presets.Count == 0)
        {
            EditorGUILayout.HelpBox("No presets found. The package's Starter Kit sample holds them; reinstall the package if it's missing.", MessageType.Warning);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Start this project's design system from a preset. Everything it makes is the project's own: " +
                                   "change any of it afterwards, and the package itself is never edited.", wrap);
        EditorGUILayout.Space();

        string[] labels = presets.Select(p => p.FromPackage ? p.name : $"{p.name}  ({p.Source})").ToArray();
        int picked = EditorGUILayout.Popup("Preset", index, labels);
        if (picked != index) Select(picked);
        if (!string.IsNullOrEmpty(Selected.description)) EditorGUILayout.LabelField(Selected.description, EditorStyles.wordWrappedMiniLabel);

        brand = EditorGUILayout.ColorField(new GUIContent("Brand colour", "Replaces the preset's Brand/Primary: the main button fill and accent text."), brand);
        font = (TMP_FontAsset)EditorGUILayout.ObjectField(new GUIContent("Font", "Every text role uses it. Empty = each text keeps its own font."),
                                                          font, typeof(TMP_FontAsset), false);
        if (font == null)
            EditorGUILayout.LabelField("No font: texts keep their own. Import TMP Essentials (Window ▸ TextMeshPro) for a default one.", EditorStyles.wordWrappedMiniLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            folder = EditorGUILayout.TextField("Folder", folder);
            if (GUILayout.Button("…", GUILayout.Width(24))) PickFolder();
        }
        documentTitle = EditorGUILayout.TextField(new GUIContent("Document title", "The heading of the generated design system document."), documentTitle);

        EditorGUILayout.Space();
        List<string> problems = DesignSystemStarterKit.Problems(Selected);
        if (!folder.Replace('\\', '/').StartsWith("Assets/")) problems.Add("The folder must be inside Assets/.");
        else
            problems.AddRange(DesignSystemStarterKit.PlannedPaths(Selected, folder).Where(File.Exists)
                .Select(p => $"{p} already exists. Pick an empty folder."));

        EditorGUILayout.LabelField("It creates", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(Summary(Selected), wrap);
        foreach (string problem in problems) EditorGUILayout.HelpBox(problem, MessageType.Error);
        EditorGUILayout.EndScrollView();

        using (new EditorGUI.DisabledScope(problems.Count > 0))
            if (GUILayout.Button("Create", GUILayout.Height(28))) Create();
    }

    private string Summary(StarterPreset p)
    {
        string sizes = string.Join(", ", p.sizes.Select(s => $"{s.name} {s.height:0.#}"));
        return $"• Theme: {p.colors.Count} colours; sizes {sizes}; {p.cornerRadius:0.#} px corners\n" +
               $"• Type scale: {Enum.GetValues(typeof(TextRole)).Length + p.typography.Keys.Count(k => !TextRoles.TryGetBuiltIn(k, out _))} roles\n" +
               $"• Button styles: {string.Join(", ", p.buttons.Select(b => b.name))}\n" +
               $"• Panel styles: {string.Join(", ", p.surfaces.Select(s => s.name))}\n" +
               "• A rounded shape sprite, settings, and a preview prefab showing every style";
    }

    private void PickFolder()
    {
        string picked = EditorUtility.OpenFolderPanel("Design system folder", "Assets", "");
        if (string.IsNullOrEmpty(picked)) return;
        string project = Path.GetFullPath(Directory.GetCurrentDirectory()).Replace('\\', '/') + "/";
        picked = picked.Replace('\\', '/');
        if (picked.StartsWith(project)) folder = picked.Substring(project.Length);
    }

    private void Create()
    {
        try
        {
            List<string> paths = DesignSystemStarterKit.Create(Selected, new DesignSystemStarterKit.Options
            {
                folder = folder, brand = brand, font = font, documentTitle = documentTitle,
            });
            Debug.Log($"[DesignSystem] Started from the {Selected.name} preset: {string.Join(", ", paths)}");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UITheme>(paths[0]);
            DesignSystemHub.Open();
            Close();
        }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
        {
            EditorUtility.DisplayDialog("Set Up Design System", e.Message, "OK");
        }
    }
}
