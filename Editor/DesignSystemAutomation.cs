using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>What setup will do, worked out before anything is changed so it can be shown first.</summary>
public class SetupPlan
{
    public DesignSystemSettings Settings;
    public ScanResult Scan;
    public List<UIComponentStyle> ExistingStyles;
    public List<StyleGenerator.Cluster> NewStyles;

    public int Total => Scan.Groups.Sum(g => g.Count);
    public int SafeWithExisting => Scan.CountApplicable(Scan.Where(MatchConfidence.Safe));
    public int SafeWithNew => NewStyles.Sum(c => Scan.CountApplicable(c.Members));
    public int SafeTotal => SafeWithExisting + SafeWithNew;
    public int ToCheck => Total - SafeTotal;
}

/// <summary>
/// Project setup in two halves: <see cref="Plan"/> scans and previews without touching anything; <see cref="Execute"/>
/// generates styles for looks nothing covers yet, applies every <see cref="MatchConfidence.Safe"/> match and writes
/// a report. The Design System hub shows the plan between the two. Also runs headless:
/// <c>Unity.exe -batchmode -projectPath . -executeMethod DesignSystemAutomation.AutoSetupBatch</c>.
/// </summary>
public static class DesignSystemAutomation
{
    public static SetupPlan Plan(DesignSystemSettings settings)
    {
        ScanResult scan = ButtonScanner.Scan(settings);
        List<UIComponentStyle> styles = DesignSystemAssets.FindAll<UIComponentStyle>();
        ButtonScanner.Suggest(scan.Groups, styles, settings.matchTolerance);
        return new SetupPlan
        {
            Settings = settings,
            Scan = scan,
            ExistingStyles = styles,
            NewStyles = StyleGenerator.BuildClusters(scan.Groups.Where(g => g.Confidence != MatchConfidence.Safe),
                                                     settings.matchTolerance, settings.minButtonsPerGeneratedStyle),
        };
    }

    /// <summary>Carries the plan out. Returns how many controls were styled.</summary>
    public static int Execute(SetupPlan plan, out string reportPath)
    {
        DesignSystemSettings settings = plan.Settings;
        UITheme theme = DesignSystemAssets.FindSingle<UITheme>() ?? CreateTheme();
        List<UIComponentStyle> created = StyleGenerator.CreateStyles(plan.NewStyles, theme, settings.recolorBakedSprites);

        List<LookGroup> unmatched = plan.Scan.Groups.Where(g => g.Confidence != MatchConfidence.Safe).ToList();
        ButtonScanner.Suggest(unmatched, plan.ExistingStyles.Concat(created).ToList(), settings.matchTolerance);

        List<LookGroup> safe = plan.Scan.Where(MatchConfidence.Safe).ToList();
        int applied = ButtonScanner.Apply(plan.Scan.Work(safe, useChoice: false));

        if (settings.defaultButtonStyle == null || settings.defaultToggleStyle == null)
        {
            if (settings.defaultButtonStyle == null) settings.defaultButtonStyle = MostUsed<UIButtonStyle>(safe);
            if (settings.defaultToggleStyle == null) settings.defaultToggleStyle = MostUsed<UIToggleStyle>(safe);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        reportPath = WriteReport(plan, created.Count, applied);
        Debug.Log($"[DesignSystem] Styled {applied} buttons & toggles and created {created.Count} style(s). Report: {reportPath}");
        return applied;
    }

    private static T MostUsed<T>(IEnumerable<LookGroup> groups) where T : UIComponentStyle =>
        groups.Where(g => g.Suggested is T).GroupBy(g => g.Suggested).OrderByDescending(s => s.Sum(g => g.Count))
              .Select(s => (T)s.Key).FirstOrDefault();

    public static void AutoSetupBatch()
    {
        try
        {
            Execute(Plan(DesignSystemSettings.GetOrCreate()), out _);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    private static UITheme CreateTheme()
    {
        Directory.CreateDirectory(DesignSystemAssets.DefaultFolder);
        var theme = ScriptableObject.CreateInstance<UITheme>();
        AssetDatabase.CreateAsset(theme, DesignSystemAssets.DefaultFolder + "/UITheme.asset");
        return theme;
    }

    // ---- Report ----

    private static string WriteReport(SetupPlan plan, int stylesCreated, int applied)
    {
        ScanResult scan = plan.Scan;
        var md = new StringBuilder();
        md.AppendLine("# Design System setup report");
        md.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm} · scenes: {plan.Settings.sceneScope}, prefabs: {(plan.Settings.scanPrefabs ? "yes" : "no")}");
        md.AppendLine();
        md.AppendLine($"- Scanned **{scan.Prefabs}** prefab(s) and **{scan.Scenes}** scene(s)");
        md.AppendLine($"- **{applied}** buttons & toggles styled with no visible change; **{stylesCreated}** style(s) created from existing buttons & toggles");
        md.AppendLine($"- **{scan.Styled}** were already styled; **{scan.InPrefabInstances}** scene buttons & toggles inside prefab instances follow their prefab");
        md.AppendLine();

        md.AppendLine("## Styles used");
        md.AppendLine("| Style | Controls |");
        md.AppendLine("|---|---|");
        foreach (var s in scan.Where(MatchConfidence.Safe).GroupBy(g => g.Suggested).OrderByDescending(s => s.Sum(g => g.Count)))
            md.AppendLine($"| {s.Key.name} | {s.Sum(g => g.Count)} |");
        md.AppendLine();

        if (ButtonScanner.LastLayoutWarnings.Count > 0)
        {
            md.AppendLine($"## Layout warnings ({ButtonScanner.LastLayoutWarnings.Count})");
            md.AppendLine("Nothing here was changed. Select it: its style component's Inspector explains each one and offers a one-click fix.");
            md.AppendLine();
            foreach (string w in ButtonScanner.LastLayoutWarnings) md.AppendLine($"- {w}");
            md.AppendLine();
        }

        List<LookGroup> check = scan.Groups.Where(g => g.Confidence != MatchConfidence.Safe).ToList();
        md.AppendLine($"## To check ({check.Sum(g => g.Count)} buttons & toggles)");
        md.AppendLine("Open **Review** from the Design System window to handle these. Toggle groups whose toggles look different are under **By toggle group**.");
        md.AppendLine();
        foreach (LookGroup g in check)
        {
            string look = g.HasBackground
                ? $"{(g.Sprite ? g.Sprite.name : "no sprite")} · #{ColorUtility.ToHtmlStringRGBA(g.Fill)} · {g.Transition}"
                : "no visible background Image";
            string suggestion = g.Suggested != null ? $" → closest: {g.Suggested.name}" : "";
            md.AppendLine($"- **{g.Count}×** {g.KindName} · {look}{suggestion}");
            foreach (ButtonRef b in g.Buttons.Take(5)) md.AppendLine($"  - {b.Display}");
            if (g.Count > 5) md.AppendLine($"  - … and {g.Count - 5} more");
        }

        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "DesignSystem");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"setup-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        File.WriteAllText(path, md.ToString());
        return path;
    }
}

/// <summary>
/// Styles buttons and toggles as they are created in the editor — from the GameObject ▸ UI menu, Add Component,
/// paste or duplicate — with their kind's default style, so new UI starts aligned instead of joining the review
/// backlog. Controls inside prefab instances are left to their prefab, and a default never goes on a control it
/// doesn't suit (a checkbox style on an option card).
/// </summary>
[InitializeOnLoad]
internal static class NewButtonAutoStyler
{
    private static readonly HashSet<GameObject> pending = new HashSet<GameObject>();

    static NewButtonAutoStyler()
    {
        ObjectFactory.componentWasAdded += c => { if (c is Selectable s && ComponentKinds.IsScannable(s)) Queue(c.gameObject); };
        ObjectChangeEvents.changesPublished += OnChanges;
    }

    private static void OnChanges(ref ObjectChangeEventStream stream)
    {
        for (int i = 0; i < stream.length; i++)
            if (stream.GetEventType(i) == ObjectChangeKind.CreateGameObjectHierarchy)
            {
                stream.GetCreateGameObjectHierarchyEvent(i, out CreateGameObjectHierarchyEventArgs e);
                Queue(EditorUtility.EntityIdToObject(e.instanceId) as GameObject);
            }
    }

    private static void Queue(GameObject go)
    {
        if (go == null || ButtonScanner.Busy || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (pending.Count == 0) EditorApplication.delayCall += Flush;
        pending.Add(go);
    }

    // Deferred so the creating tool finishes building the hierarchy (label, background) before it is styled.
    private static void Flush()
    {
        var targets = pending.Where(go => go != null).ToList();
        pending.Clear();

        DesignSystemSettings settings = DesignSystemSettings.Find();
        if (settings == null || !settings.autoStyleNewButtons) return;

        foreach (GameObject go in targets)
            foreach (Component control in ComponentKinds.Scannable(go))
            {
                if (control.TryGetComponent(out StyledElement _) || PrefabUtility.IsPartOfPrefabInstance(control)) continue;
                IComponentKind kind = ComponentKinds.For(control);
                UIComponentStyle style = settings.DefaultStyleFor(kind);
                if (style != null && kind.Suits(style, kind.Read(control))) DesignSystemAssets.Attach(control, style);
            }
    }
}
