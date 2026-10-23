using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Styling from where people already work: right-click in the Hierarchy (or a control's component menu) to style
/// what's under the selection, or to apply, remove or find a style. Applies to the selected controls — buttons,
/// toggles, input fields and dropdowns — and any inside them. Sliders and scrollbars are left out: their background
/// is a track, not a shape.
/// </summary>
public static class DesignSystemContextMenu
{
    private const string Root = "GameObject/Design System/";

    [MenuItem(Root + "Apply Style…", false, 10)]
    private static void ApplyStyle() => StylePickerWindow.Show(SelectedButtons());

    [MenuItem(Root + "Apply Style…", true)]
    private static bool CanApplyStyle() => SelectedButtons().Count > 0;

    [MenuItem(Root + "Style Children", false, 10)]
    private static void StyleChildrenOfSelection() => StyleChildren(Selection.gameObjects);

    [MenuItem(Root + "Style Children", true)]
    private static bool CanStyleChildren() => Selection.gameObjects.Any(go => go.scene.IsValid());

    /// <summary>
    /// Setup for just these objects: styles every button and toggle under them — and in the prefabs their instances
    /// come from — that a style already matches with no visible change, then opens Style Review on whatever is left.
    /// Asks first only when prefab files would be saved; changes in the open scene are one undo step.
    /// </summary>
    public static void StyleChildren(IList<GameObject> selection)
    {
        GameObject[] roots = selection.Where(go => go != null && go.scene.IsValid()).ToArray();
        if (roots.Length == 0) return;
        DesignSystemSettings settings = DesignSystemSettings.GetOrCreate();
        ScanResult scan = Scan(roots, settings);
        List<(ButtonRef target, UIComponentStyle style)> work = scan.Work(scan.Where(MatchConfidence.Safe), useChoice: false).ToList();

        List<string> prefabs = work.Where(w => w.target.Live == null).Select(w => w.target.AssetPath).Distinct().ToList();
        if (prefabs.Count > 0)
        {
            int inPrefabs = work.Count(w => w.target.Live == null);
            string names = string.Join(", ", prefabs.Take(3).Select(System.IO.Path.GetFileNameWithoutExtension)) + (prefabs.Count > 3 ? "…" : "");
            int choice = EditorUtility.DisplayDialogComplex("Style children",
                $"{work.Count - inPrefabs} here and {inPrefabs} inside {prefabs.Count} prefab(s) ({names}) can be styled with no visible change.\n\n" +
                "Styling the prefabs saves those files — commit first.", "Style all", "Cancel", "Only here");
            if (choice == 1) return;
            if (choice == 2) work = work.Where(w => w.target.Live != null).ToList();
        }

        int styled = ButtonScanner.Apply(work);
        int left = scan.Groups.Where(g => !g.IsPicture).Sum(g => g.Count) - styled;
        if (styled > 0) DesignChangeLog.Note($"styled {styled} under {roots[0].name}");
        ButtonStatusView.Invalidate();

        string scopeName = roots.Length == 1 ? roots[0].name : $"{roots.Length} selected objects";
        string message = styled > 0 ? $"Styled {styled}" : scan.Groups.Count == 0 && scan.Styled > 0 ? "Everything here is already styled" : "Nothing matched a style";
        if (left > 0) message += $" · {left} to review";
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.ShowNotification(new GUIContent(message));
        Debug.Log($"[DesignSystem] {message} under {scopeName}.");
        if (left > 0) ButtonAuditWindow.OpenFor(() => Scan(roots, settings), scopeName);
    }

    private static ScanResult Scan(GameObject[] roots, DesignSystemSettings settings)
    {
        ScanResult scan = ButtonScanner.ScanUnder(roots, includePrefabSources: true, settings);
        ButtonScanner.Suggest(scan.Groups, DesignSystemAssets.FindAll<UIComponentStyle>(), settings.matchTolerance);
        return scan;
    }

    /// <summary>The unattended part of <see cref="StyleChildren"/>: returns how many were styled and how many still need a style.</summary>
    public static (int styled, int left, ScanResult scan) StyleUnder(IEnumerable<GameObject> roots, IEnumerable<UIComponentStyle> styles,
                                                                     float tolerance, bool includePrefabSources = false)
    {
        ScanResult scan = ButtonScanner.ScanUnder(roots, includePrefabSources);
        ButtonScanner.Suggest(scan.Groups, styles, tolerance);
        int styled = ButtonScanner.Apply(scan.Work(scan.Where(MatchConfidence.Safe), useChoice: false));
        int left = scan.Groups.Where(g => !g.IsPicture).Sum(g => g.Count) - styled;
        return (styled, left, scan);
    }

    [MenuItem("CONTEXT/Selectable/Apply Design System Style…")]
    [MenuItem("CONTEXT/Image/Apply Design System Style…")]
    private static void ApplyStyleToComponent(MenuCommand command) => StylePickerWindow.Show(new List<Component> { (Component)command.context });

    [MenuItem("CONTEXT/Image/Apply Design System Style…", true)]
    private static bool CanApplyStyleToImage(MenuCommand command) => ComponentKinds.IsStyleable((Component)command.context);

    [MenuItem(Root + "New Variant…", false, 12)]
    private static void NewVariant()
    {
        UIComponentStyle common = SelectedStyled().Select(s => s.ComponentStyle).Where(s => s != null)
                                               .GroupBy(s => s).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        NewVariantWindow.Open(common, SelectedButtons());
    }

    [MenuItem(Root + "New Variant…", true)]
    private static bool CanNewVariant() => SelectedButtons().Count > 0;

    [MenuItem(Root + "Remove Style", false, 13)]
    private static void RemoveStyle()
    {
        Undo.SetCurrentGroupName("Remove button style");
        int group = Undo.GetCurrentGroup();
        foreach (StyledElement styled in SelectedStyled()) Undo.DestroyObjectImmediate(styled);
        Undo.CollapseUndoOperations(group);
    }

    [MenuItem(Root + "Remove Style", true)]
    private static bool CanRemoveStyle() => SelectedStyled().Any();

    [MenuItem(Root + "Select Style Asset", false, 14)]
    private static void SelectStyleAsset() =>
        Selection.objects = SelectedStyled().Select(s => s.ComponentStyle).Where(s => s != null).Distinct().Cast<Object>().ToArray();

    [MenuItem(Root + "Select Style Asset", true)]
    private static bool CanSelectStyleAsset() => SelectedStyled().Any(s => s.ComponentStyle != null);

    public static List<Component> SelectedButtons() => ComponentKinds.Under(Selection.gameObjects);


    private static IEnumerable<StyledElement> SelectedStyled() =>
        Selection.gameObjects.SelectMany(go => go.GetComponentsInChildren<StyledElement>(true)).Distinct();

    /// <summary>Styles the controls the style fits, as one undo step; the others are left alone.</summary>
    public static void Apply(IList<Component> buttons, UIComponentStyle style)
    {
        Undo.SetCurrentGroupName($"Apply {style.name}");
        int group = Undo.GetCurrentGroup();
        foreach (Component b in buttons.Where(b => b != null && ComponentKinds.Fits(style, b))) DesignSystemAssets.Attach(b, style);
        Undo.CollapseUndoOperations(group);
        DesignChangeLog.Note($"applied {style.name}");
        ButtonStatusView.Invalidate();
    }
}

/// <summary>A small searchable list of button styles that applies the picked one to the given buttons.</summary>
public class StylePickerWindow : EditorWindow
{
    private List<Component> targets;
    private List<UIComponentStyle> styles;
    private string search = "";
    private Vector2 scroll;
    private GUIStyle rich;
    private bool focusSearch = true;

    public static void Show(List<Component> buttons)
    {
        var window = CreateInstance<StylePickerWindow>();
        window.targets = buttons;
        window.styles = DesignSystemAssets.FindAll<UIComponentStyle>()
                                          .Where(s => buttons.Any(b => ComponentKinds.Fits(s, b))).OrderBy(s => s.name).ToList();
        window.titleContent = new GUIContent($"Apply style to {buttons.Count}");
        Rect anchor = focusedWindow != null ? focusedWindow.position : new Rect(200, 200, 400, 400);
        window.position = new Rect(anchor.x + 40f, anchor.y + 60f, 360f, 420f);
        window.ShowUtility();
    }

    private void OnGUI()
    {
        rich ??= new GUIStyle(EditorStyles.miniLabel) { richText = true };
        if (styles == null || styles.Count == 0)
        {
            EditorGUILayout.HelpBox($"No styles for these yet. Open {DesignSystemMenu.Path("Open Design System")} to set them up.", MessageType.Info);
            return;
        }

        GUI.SetNextControlName("search");
        search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
        if (focusSearch) { EditorGUI.FocusTextInControl("search"); focusSearch = false; }

        List<UIComponentStyle> shown = styles.Where(s => s != null && s.name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && shown.Count > 0) Pick(shown[0]);
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) Close();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (UIComponentStyle style in shown)
        {
            Rect row = EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            Rect r = GUILayoutUtility.GetRect(34f, 16f, GUILayout.Width(34f), GUILayout.Height(16f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 16f, 16f), StyleConsolidation.VisibleColour(style));
            Sprite sprite = style.Background.ResolveSprite(style.Theme);
            Texture preview = sprite != null ? AssetPreview.GetAssetPreview(sprite) : null;
            if (preview != null) GUI.DrawTexture(new Rect(r.x + 18f, r.y, 16f, 16f), preview, ScaleMode.ScaleToFit);
            EditorGUILayout.LabelField(style.name);
            EditorGUILayout.LabelField(DesignSystemDescribe.Short(style), rich, GUILayout.Width(130));
            EditorGUILayout.EndHorizontal();
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition)) Pick(style);
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.LabelField("Click a style, or type and press Enter.", EditorStyles.centeredGreyMiniLabel);
        if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
    }

    private void Pick(UIComponentStyle style)
    {
        DesignSystemContextMenu.Apply(targets, style);
        Close();
        GUIUtility.ExitGUI();
    }
}
