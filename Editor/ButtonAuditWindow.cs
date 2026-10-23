using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Style Review: the buttons and toggles setup could not style safely on its own. <b>By look</b> groups them by kind
/// and look, badges each group Safe or Check, and applies a style to a whole group in one click. <b>By toggle group</b>
/// lists each ToggleGroup once, so its toggles get one style — and a group whose selected toggle is drawn in another
/// colour becomes one style with that colour as its On state. Scope is the project (from
/// <see cref="DesignSystemSettings"/>) or, opened from Style Children, the selection.
/// </summary>
public class ButtonAuditWindow : EditorWindow
{
    public const string Title = "Style Review";

    [MenuItem(DesignSystemMenu.Root + Title, priority = 1)]
    public static void Open() => GetWindow<ButtonAuditWindow>(Title);

    /// <summary>Opens on a scan already made, so the hub's preview and this list show the same controls.</summary>
    public static void OpenWith(ScanResult result)
    {
        ButtonAuditWindow window = Prepare(null);
        window.scan = result;
        window.bySet = result.Sets.Any(s => !s.Ready);
        window.Repaint();
    }

    /// <summary>Opens on a scan of one scope — e.g. a selection — that the window repeats after every apply.</summary>
    public static void OpenFor(Func<ScanResult> scope, string scopeName)
    {
        ButtonAuditWindow window = Prepare(scope);
        window.scopeName = scopeName;
        window.Scan();
        window.bySet = window.scan.Sets.Any(s => !s.Ready);
    }

    private static ButtonAuditWindow Prepare(Func<ScanResult> scope)
    {
        ButtonAuditWindow window = GetWindow<ButtonAuditWindow>(Title);
        window.settings = DesignSystemSettings.GetOrCreate();
        window.scope = scope;
        window.scopeName = null;
        window.expandedLooks.Clear();
        window.expandedSets.Clear();
        return window;
    }

    [SerializeField] private bool reviewOnly = true;
    [SerializeField] private bool bySet;

    private ScanResult scan;
    private Func<ScanResult> scope;
    private string scopeName;
    private DesignSystemSettings settings;
    private Vector2 scroll;
    private readonly HashSet<LookGroup> expandedLooks = new HashSet<LookGroup>();
    private readonly HashSet<ControlSet> expandedSets = new HashSet<ControlSet>();
    private static GUIStyle badgeStyle;

    private static readonly Color SafeColour = new Color(0.30f, 0.69f, 0.31f);
    private static readonly Color CheckColour = new Color(0.96f, 0.65f, 0.14f);
    private static readonly Color NoneColour = new Color(0.55f, 0.55f, 0.55f);
    private static readonly Color PictureColour = new Color(0.35f, 0.5f, 0.75f);

    private void OnGUI()
    {
        if (settings == null) settings = DesignSystemSettings.GetOrCreate();
        DrawScope();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(scope != null ? "Rescan" : "Scan", GUILayout.Height(24))) Scan();
            int auto = scan?.CountApplicable(scan.Where(MatchConfidence.Safe)) ?? 0;
            using (new EditorGUI.DisabledScope(auto == 0))
                if (GUILayout.Button($"Style all Safe ({auto})", GUILayout.Height(24)))
                    ApplyAndRescan(scan.Work(scan.Where(MatchConfidence.Safe), useChoice: true).ToList());
            using (new EditorGUI.DisabledScope(scan == null || scan.Groups.All(g => g.Confidence == MatchConfidence.Safe)))
                if (GUILayout.Button("Generate styles for unmatched", GUILayout.Height(24))) GenerateForUnmatched();
        }

        if (scan == null)
        {
            EditorGUILayout.HelpBox($"Tip: the Design System window ({DesignSystemMenu.Path("Open Design System")}) " +
                                    "styles every safe button and toggle in one step and leaves only the ones to check here.", MessageType.Info);
            return;
        }

        string where = scope != null ? scopeName : $"{scan.Prefabs} prefab(s), {scan.Scenes} scene(s)";
        EditorGUILayout.HelpBox(
            $"{where}: {scan.Groups.Sum(g => g.Count)} unstyled — {scan.CountWhere(MatchConfidence.Safe)} safe, " +
            $"{scan.CountWhere(MatchConfidence.Check)} to check, {scan.CountWhere(MatchConfidence.None)} no match. " +
            $"{scan.Styled} already styled; {scan.InPrefabInstances} in prefab instances follow their prefab.", MessageType.None);

        if (scan.Sets.Count > 0)
            bySet = GUILayout.Toolbar(bySet ? 1 : 0, new[] { "By look", $"By toggle group ({scan.Sets.Count})" }, EditorStyles.miniButton) == 1;

        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (bySet && scan.Sets.Count > 0) DrawSets();
        else DrawLooks();
        EditorGUILayout.EndScrollView();
        if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
    }

    private void DrawScope()
    {
        if (scope != null) return;
        var so = new SerializedObject(settings);
        EditorGUI.BeginChangeCheck();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(so.FindProperty(nameof(DesignSystemSettings.sceneScope)), new GUIContent("Scenes"));
            EditorGUILayout.PropertyField(so.FindProperty(nameof(DesignSystemSettings.scanPrefabs)), new GUIContent("Prefabs"));
        }
        if (EditorGUI.EndChangeCheck()) so.ApplyModifiedProperties();
        if (GUILayout.Button("More settings…", EditorStyles.miniButton)) Selection.activeObject = settings;
    }

    // ---- By look ----

    private void DrawLooks()
    {
        int held = scan.Groups.Sum(g => g.Count) - scan.CountApplicable(scan.Groups);
        if (held > 0)
            EditorGUILayout.LabelField($"{held} toggle(s) are styled with their group, from By toggle group.", EditorStyles.miniLabel);
        reviewOnly = EditorGUILayout.ToggleLeft("Only show ones to check", reviewOnly);
        foreach (LookGroup g in scan.Groups)
            if (!reviewOnly || g.Confidence != MatchConfidence.Safe) DrawLook(g);
    }

    private void DrawLook(LookGroup g)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Swatch(g);
                if (g.IsPicture) Badge("Image", PictureColour, "The image is a picture (icon, character, checkbox), not a shape. " +
                                                               "Left as it is unless you pick a style yourself.");
                else Badge(g.Confidence == MatchConfidence.None ? "—" : g.Confidence.ToString(), ColourOf(g.Confidence), null);

                Vector2 size = g.AverageSize;
                bool open = EditorGUILayout.Foldout(expandedLooks.Contains(g), $"{g.Count} × {g.KindName} · {Describe(g)} · ~{size.x:0}×{size.y:0}", true);
                if (open) expandedLooks.Add(g); else expandedLooks.Remove(g);

                g.Choice = ClosestHint(g.Choice, g.Suggested);
                g.Choice = (UIComponentStyle)EditorGUILayout.ObjectField(g.Choice, g.Kind.StyleType, false, GUILayout.Width(160));
                using (new EditorGUI.DisabledScope(g.Choice == null || !g.HasBackground))
                    if (GUILayout.Button("Apply", GUILayout.Width(60))) ApplyAndRescan(scan.Work(new[] { g }, useChoice: true).ToList());
            }
            if (expandedLooks.Contains(g))
                foreach (ButtonRef b in g.Buttons) RefRow(b, scan.IsHeld(b) ? "styled with its group" : null);
        }
    }

    // ---- By toggle group ----

    private void DrawSets()
    {
        EditorGUILayout.LabelField("Each group gets one style, so its toggles always match.", EditorStyles.miniLabel);
        foreach (ControlSet set in scan.Sets.OrderBy(s => s.Ready).ThenBy(s => s.Name)) DrawSet(set);
    }

    private void DrawSet(ControlSet set)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            bool states = set.TryGetStates(out LookGroup off, out LookGroup on);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (LookGroup look in set.Looks.Take(3)) Swatch(look);
                if (set.Mixed) Badge($"{set.Looks.Count} looks", CheckColour, "Its toggles look different from each other.");
                else if (!set.Ready) Badge("Check", CheckColour, "One look, but no single style matches every toggle with no visible " +
                                                                 "change — e.g. a style's On colour differs from the selected toggle.");
                else Badge("Safe", SafeColour, "Every toggle matches the same style with no visible change.");

                string file = set.Members[0].Ref.AssetPath;
                string title = string.IsNullOrEmpty(file) ? set.Name : $"{set.Name} — {System.IO.Path.GetFileNameWithoutExtension(file)}";
                bool open = EditorGUILayout.Foldout(expandedSets.Contains(set), $"{title} · {set.Count} toggle(s)", true);
                if (open) expandedSets.Add(set); else expandedSets.Remove(set);

                if (set.Ready) set.Choice ??= set.Suggestion;
                set.Choice = ClosestHint(set.Choice, set.Suggestion);
                IComponentKind kind = set.Members[0].Look.Kind;
                set.Choice = (UIComponentStyle)EditorGUILayout.ObjectField(set.Choice, kind.StyleType, false, GUILayout.Width(160));
                using (new EditorGUI.DisabledScope(set.Choice == null))
                    if (GUILayout.Button(new GUIContent("Apply", "Style every toggle in the group with this style. Toggles whose image is " +
                                                                 "a picture, or that are built differently, are left as they are."), GUILayout.Width(60)))
                        ApplyAndRescan(set.Members.Where(m => ButtonScanner.CanTake(m.Look, set.Choice)).Select(m => (m.Ref, set.Choice)).ToList());
            }

            if (states)
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("The selected toggle is drawn in its own colour.", EditorStyles.miniLabel);
                    if (GUILayout.Button(new GUIContent("Make consistent",
                            "Creates one toggle style — the other toggles' look as Off, the selected one's look as On — and applies " +
                            "it to the whole group. Nothing changes on screen."), EditorStyles.miniButton, GUILayout.Width(110)))
                        MakeConsistent(set, off, on);
                }

            if (expandedSets.Contains(set))
                foreach ((ButtonRef b, LookGroup look) in set.Members) RefRow(b, b.On ? $"on · {Describe(look)}" : Describe(look));
        }
    }

    private void MakeConsistent(ControlSet set, LookGroup off, LookGroup on)
    {
        UITheme theme = DesignSystemAssets.FindSingle<UITheme>();
        if (theme == null)
        {
            EditorUtility.DisplayDialog(Title, "No theme yet — set up the project from the Design System window first.", "OK");
            return;
        }
        UIComponentStyle style = StyleGenerator.CreateForSet(set, off, on, theme, settings.recolorBakedSprites);
        DesignChangeLog.Note($"created {style.name}");
        ApplyAndRescan(set.Members.Select(m => (m.Ref, style)).ToList());
    }

    // ---- Shared ----

    // A close-but-not-safe style is offered, never pre-chosen: one click to take it, after seeing it would change something.
    private static UIComponentStyle ClosestHint(UIComponentStyle choice, UIComponentStyle closest)
    {
        if (choice != null || closest == null) return choice;
        return GUILayout.Button(new GUIContent($"Closest: {closest.name}", "Close, but applying it changes something on screen — " +
                                               "check the Ping list first."), EditorStyles.miniButton, GUILayout.Width(150)) ? closest : null;
    }

    private void RefRow(ButtonRef b, string note)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Ping", GUILayout.Width(40))) Ping(b);
            EditorGUILayout.LabelField(note == null ? b.Display : $"{b.Display}   ({note})", EditorStyles.miniLabel);
        }
    }

    private static string Describe(LookGroup g) => !g.HasBackground ? "no visible background"
        : $"{(g.Sprite != null ? g.Sprite.name : "no sprite")} · " +
          $"{(g.Radius > 0f ? $"corner ≈{g.Radius:0.#}px" : $"PPU ×{g.Multiplier:0.##}")} · #{ColorUtility.ToHtmlStringRGB(g.Fill)}";

    private static void Swatch(LookGroup g)
    {
        Rect swatch = GUILayoutUtility.GetRect(40, 28, GUILayout.Width(40));
        EditorGUI.DrawRect(swatch, g.Visible);
        Texture preview = g.Sprite != null ? AssetPreview.GetAssetPreview(g.Sprite) : null;
        if (preview != null) GUI.DrawTexture(swatch, preview, ScaleMode.ScaleToFit);
    }

    private static Color ColourOf(MatchConfidence c) => c == MatchConfidence.Safe ? SafeColour : c == MatchConfidence.Check ? CheckColour : NoneColour;

    private static void Badge(string text, Color colour, string tooltip)
    {
        badgeStyle ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        Rect r = GUILayoutUtility.GetRect(54, 18, GUILayout.Width(54));
        r.y += 5f;
        EditorGUI.DrawRect(r, colour);
        GUI.Label(r, new GUIContent(text, tooltip), badgeStyle);
    }

    private void Scan()
    {
        settings = DesignSystemSettings.GetOrCreate();
        if (scope != null) scan = scope();
        else
        {
            if (settings.sceneScope != DesignSystemSettings.SceneScope.OpenScenes && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            scan = ButtonScanner.Scan(settings);
        }
        ButtonScanner.Suggest(scan.Groups, DesignSystemAssets.FindAll<UIComponentStyle>(), settings.matchTolerance);
        expandedLooks.Clear();
        expandedSets.Clear();
        Repaint();
    }

    private void GenerateForUnmatched()
    {
        UITheme theme = DesignSystemAssets.FindSingle<UITheme>();
        if (theme == null)
        {
            EditorUtility.DisplayDialog("Generate styles", "No theme yet — set up the project from the Design System window first.", "OK");
            return;
        }

        List<StyleGenerator.Cluster> clusters = StyleGenerator.BuildClusters(
            scan.Groups.Where(g => g.Confidence != MatchConfidence.Safe), settings.matchTolerance, settings.minButtonsPerGeneratedStyle);
        if (clusters.Count == 0)
        {
            EditorUtility.DisplayDialog("Generate styles",
                $"No unmatched look is shared by {settings.minButtonsPerGeneratedStyle}+ buttons & toggles. Lower the minimum in Settings, " +
                "or pick styles for the remaining groups by hand.", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Generate styles",
                $"Create {clusters.Count} style(s) covering {clusters.Sum(c => c.Count)} buttons & toggles?\n\n" +
                string.Join("\n", clusters.Select(c => $"• {c.Name} — {c.Count}")), "Create", "Cancel"))
            return;

        StyleGenerator.CreateStyles(clusters, theme, settings.recolorBakedSprites);
        ButtonScanner.Suggest(scan.Groups, DesignSystemAssets.FindAll<UIComponentStyle>(), settings.matchTolerance);
    }

    private void ApplyAndRescan(List<(ButtonRef target, UIComponentStyle style)> work)
    {
        work = work.Where(w => w.style != null).ToList();
        int files = work.Where(w => w.target.Live == null).Select(w => w.target.AssetPath).Distinct().Count();
        if (files > 0 && !EditorUtility.DisplayDialog("Apply styles",
                $"Style {work.Count} buttons & toggles? {files} prefab/scene file(s) will be edited and saved — commit first.", "Apply", "Cancel"))
            return;

        int styled = ButtonScanner.Apply(work);
        Debug.Log($"[DesignSystem] Styled {styled} buttons & toggles.");
        DesignChangeLog.Note($"styled {styled} in review");
        ButtonStatusView.Invalidate();
        Scan();
        GUIUtility.ExitGUI();
    }

    private static void Ping(ButtonRef b)
    {
        if (b.Live != null)
        {
            Selection.activeGameObject = b.Live.gameObject;
            EditorGUIUtility.PingObject(b.Live);
        }
        else if (!string.IsNullOrEmpty(b.AssetPath))
        {
            EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(b.AssetPath));
        }
    }
}
