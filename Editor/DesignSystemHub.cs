using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The one window to start from. Overview sets the project up; Styles aligns the look; Check finds what a change
/// would break. Each tab shows only what's needed to act — explanations sit in tooltips, rarer actions in ⋯ menus.
/// </summary>
public class DesignSystemHub : EditorWindow
{
    [MenuItem(DesignSystemMenu.Root + "Open Design System", priority = 0)]
    public static void Open()
    {
        var window = GetWindow<DesignSystemHub>("Design System");
        window.minSize = new Vector2(420f, 420f);
    }

    private enum Tab { Overview, Styles, Check }

    private static readonly Color SafeColour = new Color(0.30f, 0.69f, 0.31f);
    private static readonly Color CheckColour = new Color(0.96f, 0.65f, 0.14f);
    private static readonly Color DoneColour = new Color(0.55f, 0.55f, 0.55f);

    private Tab tab;
    private SetupPlan plan;
    private string lastReport;
    private int lastStyled = -1;
    private ScanResult lastScan;
    private Vector2 scroll;

    private List<DesignIssue> issues;
    private System.DateTime lastCheck;
    private readonly HashSet<DesignIssue> fixedIssues = new HashSet<DesignIssue>();
    private readonly StyleBoard board = new StyleBoard();

    private UITheme theme;
    private List<UIComponentStyle> styles = new List<UIComponentStyle>();
    private DesignSystemSettings settings;

    private GUIStyle titleStyle, rich;

    private bool checkIsQuick;

    private void OnEnable()
    {
        tab = (Tab)SessionState.GetInt("DesignSystem.Hub.Tab", 0);
        Refresh();
        QuickCheck.Updated += Repaint;
    }

    private void OnDisable() => QuickCheck.Updated -= Repaint;

    // Assets changed underneath: a preview made before the change may no longer describe the project.
    private void OnProjectChange()
    {
        Refresh();
        plan = null;
        Repaint();
    }

    private void Refresh()
    {
        theme = DesignSystemAssets.FindSingle<UITheme>();
        styles = DesignSystemAssets.FindAll<UIComponentStyle>().OrderBy(s => s.name).ToList();
        settings = DesignSystemSettings.Find();
    }

    private bool IsSetUp => theme != null && styles.Count > 0;

    private void OnGUI()
    {
        titleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
        rich ??= new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true };

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(new GUIContent("Design System",
                "The theme holds the defaults. Each style follows the theme unless you override a field. Each button uses a style."), titleStyle);
            if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("_Menu").image, "More"), EditorStyles.label, GUILayout.Width(20)))
                MoreMenu();
        }

        int open = issues?.Count(i => !fixedIssues.Contains(i) && i.Severity != DesignIssue.Level.Note) ?? 0;
        string checkLabel = issues == null ? "Check" : open == 0 ? "Check ✓" : $"Check ({open})";
        var newTab = (Tab)GUILayout.Toolbar((int)tab, new[] { "Overview", "Styles", checkLabel }, GUILayout.Height(22));
        if (newTab != tab) SetTab(newTab);

        if (tab != Tab.Check) DrawNext();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        switch (tab)
        {
            case Tab.Overview: DrawOverview(); break;
            case Tab.Styles: DrawStyles(); break;
            case Tab.Check: DrawCheck(); break;
        }
        EditorGUILayout.EndScrollView();
        if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
    }

    private void SetTab(Tab t)
    {
        tab = t;
        SessionState.SetInt("DesignSystem.Hub.Tab", (int)tab);
    }

    private void MoreMenu()
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent(ButtonAuditWindow.Title), false, ButtonAuditWindow.Open);
        menu.AddItem(new GUIContent("Show style status in Scene view"), ButtonStatusView.Enabled, () => ButtonStatusView.Enabled = !ButtonStatusView.Enabled);
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Settings"), false, () => Selection.activeObject = DesignSystemSettings.GetOrCreate());
        menu.AddItem(new GUIContent("Save styles into all prefabs"), false, DesignSystemAssets.ReapplyProject);
        menu.AddItem(new GUIContent("Set up a new project from a preset…"), false, DesignSystemSetupWizard.Open);
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Help"), false, OpenHelp);
        menu.ShowAsContext();
    }

    // The README sits beside the module's folders, so it travels with the module into another project.
    private void OpenHelp()
    {
        string script = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        string readme = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(script)), "README.md");
        if (System.IO.File.Exists(readme)) EditorUtility.OpenWithDefaultApp(System.IO.Path.GetFullPath(readme));
        else Debug.LogWarning($"[DesignSystem] No README found at {readme}.");
    }

    // The one thing worth doing now, so nobody has to work out what comes next.
    private void DrawNext()
    {
        (string text, string tooltip, System.Action act) = NextStep();
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            Dot(act == null ? SafeColour : CheckColour);
            EditorGUILayout.LabelField(new GUIContent(text, tooltip), EditorStyles.miniBoldLabel);
            if (act != null && GUILayout.Button("Go", EditorStyles.miniButton, GUILayout.Width(40))) act();
        }
    }

    private (string text, string tooltip, System.Action act) NextStep()
    {
        if (plan != null) return ("Next: check the plan below, then Style", null, () => SetTab(Tab.Overview));
        if (!IsSetUp) return ("Next: scan to style existing buttons, toggles and panels", null, () => { SetTab(Tab.Overview); MakePlan(); });

        List<DesignIssue> quick = QuickCheck.Last?.Where(i => i.Severity != DesignIssue.Level.Note).ToList();
        if (quick != null && quick.Count > 0)
            return ($"Next: {quick.Count} issue(s) in {QuickCheck.Scope}", "Found by the automatic check of what's open, after your last change.",
                    () => { issues = QuickCheck.Last; checkIsQuick = true; fixedIssues.Clear(); lastCheck = System.DateTime.Now; SetTab(Tab.Check); });

        int unnamed = StyleRenamer.Unnamed(styles).Count;
        if (unnamed > 0)
            return ($"Next: name {unnamed} generated style(s) by role", "E.g. Button_Orange_SemiRounded → Button_Primary. Only names change.",
                    StyleRenamer.NameGeneratedByRole);

        if (issues == null || checkIsQuick || DesignChangeLog.Entries.Count > 0)
            return ("Next: run the full check before committing", DesignChangeLog.Entries.Count > 0 ? "Changed: " + string.Join(", ", DesignChangeLog.Entries) : null,
                    () => { SetTab(Tab.Check); RunCheck(); });

        int open = issues.Count(i => !fixedIssues.Contains(i) && i.Severity != DesignIssue.Level.Note);
        if (open > 0) return ($"Next: {open} issue(s) on the Check tab", null, () => SetTab(Tab.Check));
        return ("All aligned ✓", null, null);
    }

    // ---- Overview: the checklist ----

    private const int TidyTarget = 8;
    private GUIStyle doneStyle, todoStyle;

    private void DrawOverview()
    {
        if (plan != null)
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                DrawPlan();

        doneStyle ??= new GUIStyle(EditorStyles.label) { normal = { textColor = SafeColour } };
        todoStyle ??= new GUIStyle(EditorStyles.label) { normal = { textColor = DoneColour } };

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool done = IsSetUp;
                Step(done, "Style existing UI", done ? $"{styles.Count} styles" : "not started",
                     "Scans for buttons, toggles and panels and styles them with no visible change, creating styles from how they look.");
                if (GUILayout.Button(new GUIContent($"{ScopeName()} ▾", "Which scenes and prefabs to scan."), EditorStyles.miniPullDown, GUILayout.Width(150)))
                    ScopeMenu();
                if (GUILayout.Button(done ? "Find more" : "Scan", EditorStyles.miniButton, GUILayout.Width(70))) MakePlan();
            }
            if (lastStyled >= 0)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(22);
                    EditorGUILayout.LabelField($"Styled {lastStyled}", EditorStyles.miniLabel);
                    if (!string.IsNullOrEmpty(lastReport) && GUILayout.Button("Report", EditorStyles.miniButton, GUILayout.Width(60)))
                        EditorUtility.OpenWithDefaultApp(lastReport);
                    int toReview = lastScan?.Groups.Where(g => g.Confidence != MatchConfidence.Safe && !g.IsPicture).Sum(g => g.Count) ?? 0;
                    if (toReview > 0 && GUILayout.Button($"Review {toReview}", EditorStyles.miniButton, GUILayout.Width(80)))
                        ButtonAuditWindow.OpenWith(lastScan);
                }
            if (theme == null)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(22);
                    EditorGUILayout.LabelField(new GUIContent("New project with no UI yet? Start from a preset instead.",
                        "Creates a theme, type scale and core button styles from a preset, then styles new buttons with them."), EditorStyles.miniLabel);
                    if (GUILayout.Button("Set up…", EditorStyles.miniButton, GUILayout.Width(70))) DesignSystemSetupWizard.Open();
                }

            using (new EditorGUI.DisabledScope(!IsSetUp))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool tidy = IsSetUp && styles.GroupBy(ComponentKinds.For).All(k => k.Count() <= TidyTarget);
                    Step(tidy, "Tidy up styles", $"{styles.Count} styles",
                         $"Merge near-duplicates and remove legacy styles until a few roles remain (≤ {TidyTarget} per kind).");
                    if (GUILayout.Button("Tidy up", EditorStyles.miniButton, GUILayout.Width(70))) StyleConsolidatorWindow.Open();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool set = theme != null && theme.ButtonHeight > 0f;
                    Step(set, "Set button height", set ? $"{theme.ButtonHeight:0.#} px" : "not set", "One height for every style that uses the theme height.");
                    if (GUILayout.Button("Styles", EditorStyles.miniButton, GUILayout.Width(70))) SetTab(Tab.Styles);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool set = theme != null && theme.SetButtonLabelFont;
                    string text = set && theme.Typography != null && theme.Typography.Get(theme.ButtonLabelRoleId) is TypographyScale.RoleStyle rs
                        ? $"{theme.ButtonLabelRoleId} {rs.fontSize:0.#} px" : "not set";
                    Step(set, "Set button text", text, "One text role and size for every style that uses the theme font.");
                    if (GUILayout.Button("Styles", EditorStyles.miniButton, GUILayout.Width(70))) SetTab(Tab.Styles);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    (bool passed, string status) = CheckStatus();
                    Step(passed, "Pass the check", status, "Nothing a player would see as broken, since the last change.");
                    if (GUILayout.Button("Check", EditorStyles.miniButton, GUILayout.Width(70))) { SetTab(Tab.Check); RunCheck(); }
                }
                if (settings != null)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool on = settings.autoStyleNewButtons && settings.defaultButtonStyle != null;
                        Step(on, "Style new buttons & toggles", null, "Buttons created in the editor get this style; toggles get the toggle style " +
                                                                    "setup picked (Settings ▸ Default Toggle Style).");
                        var so = new SerializedObject(settings);
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(so.FindProperty(nameof(DesignSystemSettings.autoStyleNewButtons)), GUIContent.none, GUILayout.Width(16));
                        EditorGUILayout.PropertyField(so.FindProperty(nameof(DesignSystemSettings.defaultButtonStyle)), GUIContent.none, GUILayout.Width(150));
                        if (EditorGUI.EndChangeCheck()) so.ApplyModifiedProperties();
                    }
            }
        }
    }

    private void Step(bool done, string title, string status, string tooltip)
    {
        EditorGUILayout.LabelField(new GUIContent(done ? "✓" : "○", tooltip), done ? doneStyle : todoStyle, GUILayout.Width(16));
        EditorGUILayout.LabelField(new GUIContent(title, tooltip), GUILayout.Width(190));
        EditorGUILayout.LabelField(status ?? "", EditorStyles.miniLabel);
    }

    private (bool passed, string status) CheckStatus()
    {
        if (issues == null) return (false, "not run");
        if (checkIsQuick) return (false, "open scene only");
        if (DesignChangeLog.Entries.Count > 0) return (false, "changed since last check");
        int open = issues.Count(i => !fixedIssues.Contains(i) && i.Severity != DesignIssue.Level.Note);
        return open == 0 ? (true, $"passed {lastCheck:HH:mm}") : (false, $"{open} issue(s)");
    }

    private string ScopeName()
    {
        DesignSystemSettings s = settings != null ? settings : DesignSystemSettings.GetOrCreate();
        settings = s;
        string scenes = s.sceneScope switch
        {
            DesignSystemSettings.SceneScope.OpenScenes => "open scenes",
            DesignSystemSettings.SceneScope.BuildScenes => "build scenes",
            _ => "all scenes",
        };
        return s.scanPrefabs ? scenes + " + prefabs" : scenes;
    }

    private void ScopeMenu()
    {
        var menu = new GenericMenu();
        foreach (DesignSystemSettings.SceneScope scope in System.Enum.GetValues(typeof(DesignSystemSettings.SceneScope)))
        {
            DesignSystemSettings.SceneScope chosen = scope;
            string label = scope switch
            {
                DesignSystemSettings.SceneScope.OpenScenes => "Open scenes only",
                DesignSystemSettings.SceneScope.BuildScenes => "Scenes in Build Settings",
                _ => "Every scene in the project",
            };
            menu.AddItem(new GUIContent(label), settings.sceneScope == scope, () => { settings.sceneScope = chosen; EditorUtility.SetDirty(settings); });
        }
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Include prefabs"), settings.scanPrefabs, () => { settings.scanPrefabs = !settings.scanPrefabs; EditorUtility.SetDirty(settings); });
        menu.AddItem(new GUIContent("Folders to include or skip…"), false, () => Selection.activeObject = settings);
        menu.ShowAsContext();
    }

    private void MakePlan()
    {
        settings = DesignSystemSettings.GetOrCreate();
        if (settings.sceneScope != DesignSystemSettings.SceneScope.OpenScenes && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        plan = DesignSystemAutomation.Plan(settings);
        lastStyled = -1;
        GUIUtility.ExitGUI();
    }

    private void DrawPlan()
    {
        int pictures = plan.Scan.Pictures;
        string byKind = string.Join(" · ", plan.Scan.Groups.GroupBy(g => g.Kind.DisplayName).Select(k => $"{k.Sum(g => g.Count)} {k.Key.ToLowerInvariant()}s"));
        EditorGUILayout.LabelField(new GUIContent($"Found {plan.Total + plan.Scan.Styled} to style", $"Not styled yet: {byKind}"), EditorStyles.boldLabel);
        EditorGUILayout.LabelField(byKind, EditorStyles.miniLabel);
        Line(SafeColour, $"<b>{plan.SafeTotal}</b> styled with no visible change" + (plan.NewStyles.Count > 0 ? $" ({plan.NewStyles.Count} new styles)" : ""),
             plan.NewStyles.Count > 0 ? "New styles: " + string.Join(", ", plan.NewStyles.Select(c => $"{c.Name} ({c.Count})")) : null);
        Line(CheckColour, $"<b>{plan.ToCheck - pictures}</b> to review", "They look different enough that a person should pick their style.");
        if (pictures + plan.Scan.Styled > 0)
            Line(DoneColour, $"{pictures} pictures left alone · {plan.Scan.Styled} already styled", "Icons, characters and checkboxes are never restyled automatically.");

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(plan.SafeTotal == 0))
                if (GUILayout.Button(new GUIContent($"Style {plan.SafeTotal}", "Saves prefabs and scenes directly — commit first."), GUILayout.Height(26)))
                    ExecutePlan();
            if (GUILayout.Button("Review…", GUILayout.Height(26), GUILayout.Width(80))) ButtonAuditWindow.OpenWith(plan.Scan);
            if (GUILayout.Button("Cancel", GUILayout.Height(26), GUILayout.Width(70))) plan = null;
        }
    }

    private void ExecutePlan()
    {
        SetupPlan current = plan;
        plan = null;
        lastStyled = DesignSystemAutomation.Execute(current, out lastReport);
        lastScan = current.Scan;
        DesignChangeLog.Note($"setup styled {lastStyled} button(s)");
        Refresh();
        GUIUtility.ExitGUI();
    }

    // ---- Styles ----

    private void DrawStyles()
    {
        if (!IsSetUp)
        {
            EditorGUILayout.HelpBox("Set the project up on the Overview tab first.", MessageType.Info);
            return;
        }
        List<UIComponentStyle> broken = styles.Where(DesignSystemRepair.NeedsRepair).ToList();
        if (broken.Count > 0)
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(EditorGUIUtility.IconContent("console.warnicon.sml"), GUILayout.Width(18));
                EditorGUILayout.LabelField(new GUIContent($"{broken.Count} style(s) style a picture as a shape",
                    "They recolour an icon, character or checkbox flat, or stretch it to button height."), EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("Restore", "Back to their original look. Re-saves the prefabs using them — commit first."),
                                     EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    DesignSystemRepair.Repair(broken);
                    DesignChangeLog.Note("restored picture styles");
                    Refresh();
                    GUIUtility.ExitGUI();
                }
            }
        board.Draw(theme, styles);
    }

    // ---- Check ----

    private void DrawCheck()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent(issues == null ? "Run check" : "Run again",
                    "Finds labels that no longer fit, layouts that fight a size, pictures styled as shapes and deleted colours, " +
                    "in every prefab plus the scenes chosen on Overview. Read-only — every fix is a separate click."), GUILayout.Height(26)))
                RunCheck();
            if (issues != null) EditorGUILayout.LabelField(checkIsQuick ? $"Open scene only" : $"Last run {lastCheck:HH:mm}", EditorStyles.miniLabel, GUILayout.Width(100));
        }
        if (issues == null) return;

        List<DesignIssue> open = issues.Where(i => !fixedIssues.Contains(i)).ToList();
        if (open.Count == 0)
        {
            EditorGUILayout.HelpBox("Nothing looks broken.", MessageType.Info);
            return;
        }

        List<DesignIssue> safe = open.Where(i => i.FixKeepsLook && i.Fix != null).ToList();
        if (safe.Count > 0 && GUILayout.Button(new GUIContent($"Fix {safe.Count} safe issue(s)",
                "Only fixes that never change how anything looks: restoring pictures styled as shapes, and removing redundant " +
                "fitter axes. Everything else needs a look first."), GUILayout.Height(24)))
            FixSafe(safe);

        foreach (IGrouping<string, DesignIssue> group in open.GroupBy(i => i.Category))
        {
            string key = "DesignSystem.Check." + group.Key;
            DesignIssue.Level level = group.Min(i => i.Severity);
            bool expandedGroup = EditorGUILayout.Foldout(SessionState.GetBool(key, level == DesignIssue.Level.Problem),
                new GUIContent($" {group.Key} ({group.Count()})", Icon(level)), true);
            SessionState.SetBool(key, expandedGroup);
            if (expandedGroup)
                foreach (DesignIssue issue in group) DrawIssue(issue);
        }
    }

    private void DrawIssue(DesignIssue issue)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(issue.Where, issue.Where), EditorStyles.miniBoldLabel);
                if (GUILayout.Button(issue.Target != null ? "Select" : "Show", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    if (issue.Target != null)
                    {
                        Selection.activeObject = issue.Target;
                        EditorGUIUtility.PingObject(issue.Target);
                    }
                    else if (!string.IsNullOrEmpty(issue.AssetPath))
                        EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(issue.AssetPath));
                }
                if (issue.Fix != null && GUILayout.Button(new GUIContent("Fix", issue.FixLabel), EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    issue.Fix();
                    fixedIssues.Add(issue);
                    DesignChangeLog.Note($"fixed: {issue.Category.ToLowerInvariant()}");
                    Refresh();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.LabelField(issue.Message, EditorStyles.wordWrappedMiniLabel);
        }
    }

    private void FixSafe(List<DesignIssue> safe)
    {
        string summary = string.Join("\n", safe.GroupBy(i => i.Category).Select(g => $"• {g.Count()} × {g.Key}"));
        if (!EditorUtility.DisplayDialog("Fix safe issues",
                $"Apply {safe.Count} fix(es) that don't change how anything looks?\n\n{summary}\n\n" +
                "Prefabs are re-saved — commit first so the change is easy to review.", "Fix", "Cancel"))
            return;
        foreach (DesignIssue issue in DesignSystemCheck.FixSafe(safe)) fixedIssues.Add(issue);
        DesignChangeLog.Note("fixed safe issues");
        Refresh();
        GUIUtility.ExitGUI();
    }

    private void RunCheck()
    {
        DesignSystemSettings s = settings != null ? settings : DesignSystemSettings.GetOrCreate();
        if (s.sceneScope != DesignSystemSettings.SceneScope.OpenScenes && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        issues = DesignSystemCheck.Run(s);
        checkIsQuick = false;
        fixedIssues.Clear();
        lastCheck = System.DateTime.Now;
        DesignChangeLog.Clear();
        GUIUtility.ExitGUI();
    }

    private static Texture Icon(DesignIssue.Level level) => EditorGUIUtility.IconContent(level switch
    {
        DesignIssue.Level.Problem => "console.erroricon.sml",
        DesignIssue.Level.Warning => "console.warnicon.sml",
        _ => "console.infoicon.sml",
    }).image;

    // ---- Drawing helpers ----

    private void Line(Color dot, string text, string tooltip)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            Dot(dot);
            EditorGUILayout.LabelField(new GUIContent(text, tooltip), rich);
        }
    }

    private static void Dot(Color colour)
    {
        Rect r = GUILayoutUtility.GetRect(10, 10, GUILayout.Width(10));
        r.y += 4f;
        r.height = 8f;
        r.width = 8f;
        EditorGUI.DrawRect(r, colour);
    }
}
