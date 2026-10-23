using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// The check, run on its own after every design change — but only over what is open: the prefab being edited, or
/// else the loaded scenes. Cheap enough to need no button; the full project check stays on the Check tab.
/// </summary>
public static class QuickCheck
{
    private const double Settle = 0.8;
    private static double runAt = -1;

    /// <summary>Issues found in what was open at the last run, or null before the first.</summary>
    public static List<DesignIssue> Last { get; private set; }

    /// <summary>What the last run covered, e.g. a prefab or scene name.</summary>
    public static string Scope { get; private set; }

    public static event Action Updated;

    /// <summary>
    /// Runs once edits have paused, however many changes came before — dragging a colour in the style Inspector
    /// reports a change every frame.
    /// </summary>
    public static void Schedule()
    {
        if (runAt < 0) EditorApplication.update += Tick;
        runAt = EditorApplication.timeSinceStartup + Settle;
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < runAt) return;
        EditorApplication.update -= Tick;
        runAt = -1;
        if (!EditorApplication.isPlayingOrWillChangePlaymode) Run();
    }

    public static void Run()
    {
        var issues = new List<DesignIssue>();
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null)
        {
            Scope = stage.prefabContentsRoot.name;
            DesignSystemCheck.CheckHierarchy(stage.prefabContentsRoot, new CheckScope { Label = Scope, AssetPath = stage.assetPath, Live = true }, issues);
        }
        else
        {
            var names = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                names.Add(scene.name);
                var scope = new CheckScope { Label = scene.name, AssetPath = scene.path, Live = true };
                foreach (UnityEngine.GameObject root in scene.GetRootGameObjects()) DesignSystemCheck.CheckHierarchy(root, scope, issues);
            }
            Scope = string.Join(", ", names);
        }
        Last = issues;
        Updated?.Invoke();
    }
}
