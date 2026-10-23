using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Runs the design check before a build, so a clipped label or a picture recoloured flat is caught before it
/// ships rather than by a player. Scenes are left out: opening them mid-build would disturb the build itself.
/// Interactive builds may be cancelled from the dialog; batch builds (CI) only log, and never fail on design.
/// </summary>
public class DesignSystemBuildCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        DesignSystemSettings settings = DesignSystemSettings.Find();
        if (settings == null || !settings.checkBeforeBuild) return;

        var problems = DesignSystemCheck.Run(settings, includeScenes: false)
                                        .Where(i => i.Severity == DesignIssue.Level.Problem).ToList();
        if (problems.Count == 0) return;

        string list = string.Join("\n", problems.Take(15).Select(p => $"• {p.Category}: {p.Where}"));
        Debug.LogWarning($"[DesignSystem] {problems.Count} design problem(s) found before build:\n{list}");

        if (Application.isBatchMode) return;
        if (!EditorUtility.DisplayDialog("Design check",
                $"{problems.Count} design problem(s) found — for example labels that no longer fit their button.\n\n{list}\n\n" +
                "Open the Design System window's Check tab to see and fix them.", "Build anyway", "Cancel build"))
            throw new BuildFailedException("Build cancelled from the design check.");
    }
}
