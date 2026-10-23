using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Inspector for <see cref="StyledText"/>: auto-wires the one project-wide
/// <see cref="TypographyScale"/> when empty, and re-applies live as you change the
/// role / override in the Inspector so you see the result immediately.
/// </summary>
[CustomEditor(typeof(StyledText))]
[CanEditMultipleObjects]
public class StyledTextEditor : Editor
{
    private void OnEnable()
    {
        SerializedProperty scaleProp = serializedObject.FindProperty("scale");
        if (scaleProp.objectReferenceValue == null)
        {
            TypographyScale scale = TypographyAssets.Find();
            if (scale != null)
            {
                scaleProp.objectReferenceValue = scale;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                ApplyToTargets();
            }
        }
    }

    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck()) ApplyToTargets();

        EditorGUILayout.Space();
        if (GUILayout.Button("Re-apply from Typography Scale")) ApplyToTargets();
    }

    private void ApplyToTargets()
    {
        foreach (Object t in targets)
        {
            var st = (StyledText)t;
            st.Apply();
            var tmp = st.GetComponent<TMP_Text>();
            if (tmp != null) EditorUtility.SetDirty(tmp);
        }
    }
}

/// <summary>
/// Inspector for <see cref="TypographyScale"/>. Edits are pushed live to every
/// StyledText on screen (open scenes AND a prefab open in isolation) — so with the
/// preview prefab open, tuning a role updates it instantly.
/// </summary>
[CustomEditor(typeof(TypographyScale))]
public class TypographyScaleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        bool changed = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Open TypographyPreview.prefab (next to this asset) to see the roles render as you edit.\n" +
            $"Generate/refresh it via {DesignSystemMenu.Path("Create or Refresh Preview Prefab", DesignSystemMenu.TypographyRoot)}.",
            MessageType.Info);
        if (GUILayout.Button("Apply to all StyledText (open scenes + prefab stage)"))
            TypographyAssets.ReapplyEverywhere();

        DrawAddRole((TypographyScale)target);

        if (changed)
        {
            DesignChangeLog.Note("type scale");
            TypographyAssets.ReapplyEverywhere();
            DesignSystemAssets.ReapplyOpen();
        }

        DrawButtonStyleUsers();
    }

    private string newRole = "";
    private string copyFrom = nameof(TextRole.Body);

    // The list's own + copies the last entry, name and all; this gives the new role its own name from the start.
    private void DrawAddRole(TypographyScale scale)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(new GUIContent("Add a role", "A text role of this project's own. Texts and styles pick it like the built-in ones."),
                                   EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            newRole = EditorGUILayout.TextField(newRole);
            EditorGUILayout.LabelField("starting from", GUILayout.Width(84));
            copyFrom = TextRoleField.Popup(copyFrom, scale, GUILayout.Width(120));
            string name = newRole.Trim();
            bool taken = scale.Get(name) != null;
            using (new EditorGUI.DisabledScope(name.Length == 0 || taken))
                if (GUILayout.Button(taken ? "Name taken" : "Add", GUILayout.Width(80)))
                {
                    Undo.RecordObject(scale, "Add text role");
                    scale.AddRole(name, copyFrom);
                    EditorUtility.SetDirty(scale);
                    AssetDatabase.SaveAssetIfDirty(scale);
                    DesignChangeLog.Note($"added text role {name}");
                    newRole = "";
                }
        }
    }

    // Answers "which buttons does this role size?" — the question a role edit can't otherwise answer.
    private void DrawButtonStyleUsers()
    {
        var byRole = new System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.List<string>>();
        foreach (UIButtonStyle style in DesignSystemAssets.FindAll<UIButtonStyle>())
            if (style.TryGetLabelRoleId(out string role))
            {
                if (!byRole.TryGetValue(role, out var names)) byRole[role] = names = new System.Collections.Generic.List<string>();
                names.Add(style.name + (style.LabelFontSource == LabelFontSource.Theme ? " (theme)" : ""));
            }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Button styles using each role", EditorStyles.boldLabel);
        if (byRole.Count == 0)
        {
            EditorGUILayout.LabelField("No button style sets its label font yet — button labels keep their own fonts.", EditorStyles.wordWrappedMiniLabel);
            return;
        }
        foreach (var pair in byRole)
            EditorGUILayout.LabelField($"{pair.Key}: {string.Join(", ", pair.Value)}", EditorStyles.wordWrappedMiniLabel);
    }
}

/// <summary>Shared editor helpers for the typography system.</summary>
public static class TypographyAssets
{
    /// <summary>The single TypographyScale asset in the project, or null if none/ambiguous.</summary>
    public static TypographyScale Find()
    {
        string[] guids = AssetDatabase.FindAssets("t:TypographyScale");
        if (guids.Length == 0) return null;
        if (guids.Length > 1)
            Debug.LogWarning("[Typography] More than one TypographyScale asset exists — StyledText auto-wire " +
                             "picks the first. Keep a single source of truth.");
        return AssetDatabase.LoadAssetAtPath<TypographyScale>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    [MenuItem(DesignSystemMenu.TypographyRoot + "Apply To Open Scene(s) And Prefab Stage")]
    public static void ReapplyEverywhere()
    {
        int n = 0;

        foreach (StyledText st in Object.FindObjectsByType<StyledText>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            n += ApplyOne(st);

        // A prefab open in isolation (e.g. the preview prefab) lives in its own stage,
        // which FindObjectsByType doesn't cover — reach it explicitly.
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null)
            foreach (StyledText st in stage.prefabContentsRoot.GetComponentsInChildren<StyledText>(true))
                n += ApplyOne(st);

        Debug.Log($"[Typography] Applied the scale to {n} StyledText component(s).");
    }

    private static int ApplyOne(StyledText st)
    {
        st.Apply();
        var tmp = st.GetComponent<TMP_Text>();
        if (tmp != null) EditorUtility.SetDirty(tmp);
        return 1;
    }
}
