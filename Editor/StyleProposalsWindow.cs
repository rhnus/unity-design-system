using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Where a designer and a developer approve style proposals before anything changes: each control's before and
/// proposed captures side by side, why it was proposed, and whether its layout would change. Only
/// <b>Apply approved</b> edits prefabs and scenes; a proposal that changes the layout also needs that ticked.
/// </summary>
public class StyleProposalsWindow : EditorWindow
{
    private const string Title = "Style Proposals";
    private static readonly string[] Filters = { "To review", "Approved", "Rejected", "Applied", "All" };

    [MenuItem(DesignSystemMenu.Root + Title, priority = 3)]
    public static void Open() => Show(StyleProposals.Latest());

    public static void Show(string folder)
    {
        var window = GetWindow<StyleProposalsWindow>(Title);
        window.minSize = new Vector2(560f, 400f);
        window.Load(folder);
    }

    private string folder;
    private ProposalRun run;
    private int filter;
    private Vector2 scroll;
    private GUIStyle wrap;
    private readonly Dictionary<string, Texture2D> images = new Dictionary<string, Texture2D>();

    private void Load(string runFolder)
    {
        folder = runFolder;
        ClearImages();
        run = folder != null && File.Exists(StyleProposals.ReviewPath(folder)) ? StyleProposals.Load(folder) : null;
        Repaint();
    }

    private void Save() => StyleProposals.Save(run, folder);

    private void OnDisable() => ClearImages();

    private void ClearImages()
    {
        foreach (Texture2D t in images.Values) if (t != null) DestroyImmediate(t);
        images.Clear();
    }

    private void OnGUI()
    {
        wrap ??= new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };
        if (run == null)
        {
            EditorGUILayout.HelpBox("No proposals yet. Ask Claude to run the style-review skill, or run StyleProposals.Audit and judge the items.", MessageType.Info);
            if (GUILayout.Button("Open a run folder…")) PickFolder();
            return;
        }

        DrawHeader();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (ProposalItem item in Visible()) DrawItem(item);
        EditorGUILayout.EndScrollView();
        DrawFooter();
    }

    private void DrawHeader()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"<b>{run.scope}</b> · {run.created} · {run.items.Count} control(s) to review", wrap);
            if (GUILayout.Button("Other run…", EditorStyles.miniButton, GUILayout.Width(80))) PickFolder();
            if (GUILayout.Button("Reload", EditorStyles.miniButton, GUILayout.Width(60))) Load(folder);
        }
        EditorGUILayout.LabelField(
            $"Also scanned: {run.alreadyStyled} already styled, {run.safeMatches} that a style fits with no visible change (setup styles those), " +
            $"{run.pictures} pictures, {run.unique} marked unique.", EditorStyles.wordWrappedMiniLabel);
        filter = GUILayout.Toolbar(filter, Filters);
        EditorGUILayout.Space(4);
    }

    private IEnumerable<ProposalItem> Visible() => run.items.Where(i => filter switch
    {
        0 => string.IsNullOrEmpty(i.applied) && i.review == "pending",
        1 => string.IsNullOrEmpty(i.applied) && i.review == "approved",
        2 => i.review == "rejected",
        3 => !string.IsNullOrEmpty(i.applied),
        _ => true,
    });

    private void DrawItem(ProposalItem item)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"<b>{item.name}</b>  ·  {item.kind}  ·  {item.size}", wrap);
                if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(50))) Reveal(item);
            }
            EditorGUILayout.LabelField(item.where + (item.hiddenByDefault ? "  (on a panel that's hidden until shown)" : ""), EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(item.currentStyle)) EditorGUILayout.LabelField($"Styled now with {item.currentStyle}", EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(item.previewText))
                EditorGUILayout.LabelField($"Captured with sample text: “{item.previewText}”", EditorStyles.wordWrappedMiniLabel);
            if (item.others != null && item.others.Count > 0)
                EditorGUILayout.LabelField(new GUIContent($"Applies to {item.others.Count + 1} controls that look exactly like this one; the first is shown.",
                    string.Join("\n", item.others.Take(15).Select(o => o.where)) + (item.others.Count > 15 ? $"\n… and {item.others.Count - 15} more" : "")),
                    EditorStyles.miniBoldLabel);

            DrawImages(item);
            EditorGUILayout.LabelField($"<b>{Proposal(item)}</b>", wrap);
            if (!string.IsNullOrEmpty(item.reason)) EditorGUILayout.LabelField(item.reason, wrap);
            DrawImpact(item);

            if (!string.IsNullOrEmpty(item.applied))
            {
                EditorGUILayout.LabelField($"Applied {item.applied}", EditorStyles.miniBoldLabel);
                return;
            }
            DrawReview(item);
        }
    }

    private void DrawImages(ProposalItem item)
    {
        Texture2D before = Image(item.before);
        Texture2D after = item.decision == "unique" || item.decision == "skip" ? null : Image(item.proposalImage);
        if (before == null && after == null) return;
        float half = (position.width - 48f) / 2f;
        using (new EditorGUILayout.HorizontalScope())
        {
            Picture("Now", before, half);
            if (after != null) Picture("Proposed", after, half);
        }
    }

    private static void Picture(string caption, Texture2D tex, float width)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(width)))
        {
            EditorGUILayout.LabelField(caption, EditorStyles.miniBoldLabel);
            if (tex == null) return;
            float height = Mathf.Min(260f, width * tex.height / tex.width);
            Rect r = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
        }
    }

    private Texture2D Image(string file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        if (images.TryGetValue(file, out Texture2D cached)) return cached;
        string path = Path.Combine(folder, file);
        Texture2D tex = null;
        if (File.Exists(path))
        {
            tex = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            tex.LoadImage(File.ReadAllBytes(path));
        }
        images[file] = tex;
        return tex;
    }

    public static string Proposal(ProposalItem item) => item.decision switch
    {
        "apply" => $"Use {item.decisionStyle}" + (item.keepOwnSize ? ", keeping its own size" : ""),
        "custom" => $"New style {item.custom.name}: a variant of {item.custom.baseStyle}" + CustomKeeps(item.custom),
        "unique" => "Unique by design: leave its look as it is, and stop proposing styles for it",
        "skip" => "Leave as it is for now",
        _ => "Not judged yet",
    };

    private static string CustomKeeps(CustomStyleSpec spec)
    {
        var keeps = new List<string>();
        if (spec.keepSize) keeps.Add("its own size");
        if (spec.height > 0f) keeps.Add($"{spec.height:0}px height");
        if (spec.square) keeps.Add("a square shape");
        if (spec.keepLabelFont) keeps.Add("its own label font");
        if (spec.labelSize > 0f) keeps.Add($"{spec.labelSize:0}px labels");
        if (spec.growWithContent) keeps.Add("a height that grows with its text");
        if (!string.IsNullOrEmpty(spec.labelRole)) keeps.Add($"{spec.labelRole} labels");
        return keeps.Count > 0 ? " with " + string.Join(", ", keeps) : "";
    }

    private void DrawImpact(ProposalItem item)
    {
        if (item.decision != "apply" && item.decision != "custom") return;
        if (string.IsNullOrEmpty(item.proposalImage))
        {
            EditorGUILayout.HelpBox("No capture of this proposal yet. Prepare the run to render it.", MessageType.None);
            return;
        }
        if (!item.proposalImpact.changes)
        {
            EditorGUILayout.LabelField("✓ Layout unchanged: nothing around it moves.", EditorStyles.miniBoldLabel);
            return;
        }
        EditorGUILayout.HelpBox($"Changes the layout: {item.proposalImpact.detail}.", MessageType.Warning);
        item.acceptLayoutChange = EditorGUILayout.ToggleLeft("I've checked the layout change and accept it", item.acceptLayoutChange);
    }

    private void DrawReview(ProposalItem item)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Note", GUILayout.Width(34));
            item.reviewNote = EditorGUILayout.TextField(item.reviewNote);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            string state = item.review == "approved" ? $"✓ Approved by {item.reviewer}" : item.review == "rejected" ? $"✗ Rejected by {item.reviewer}" : "";
            EditorGUILayout.LabelField(state, EditorStyles.miniLabel, GUILayout.Width(220));
            using (new EditorGUI.DisabledScope(item.decision == "" || item.decision == "skip"))
                if (GUILayout.Button("Approve", GUILayout.Width(80))) Mark(item, "approved");
            if (GUILayout.Button("Reject", GUILayout.Width(70))) Mark(item, "rejected");
            if (item.review != "pending" && GUILayout.Button("Undo", GUILayout.Width(50))) Mark(item, "pending");
        }
    }

    private void Mark(ProposalItem item, string review)
    {
        item.review = review;
        item.reviewer = review == "pending" ? "" : Environment.UserName;
        Save();
    }

    private void DrawFooter()
    {
        int ready = run.items.Count(StyleProposals.Ready);
        int waiting = run.items.Count(i => i.review == "approved" && string.IsNullOrEmpty(i.applied) && !StyleProposals.Ready(i));
        if (waiting > 0)
            EditorGUILayout.HelpBox($"{waiting} approved proposal(s) change the layout and wait for that to be accepted too.", MessageType.None);
        if (EditorApplication.isPlaying) EditorGUILayout.HelpBox("Stop Play mode to apply.", MessageType.None);
        using (new EditorGUI.DisabledScope(ready == 0 || EditorApplication.isPlaying))
            if (GUILayout.Button($"Apply {ready} approved", GUILayout.Height(28))) ApplyApproved(ready);
    }

    private void ApplyApproved(int ready)
    {
        Save();
        if (!EditorUtility.DisplayDialog(Title,
                $"Apply {ready} approved proposal(s)? This edits and saves their prefabs and scenes and can't be undone from Unity — commit first.",
                "Apply", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        int changed = StyleProposals.ApplyApproved(folder);
        Load(folder);
        ShowNotification(new GUIContent($"{changed} control(s) changed"));
        GUIUtility.ExitGUI();
    }

    private void PickFolder()
    {
        string picked = EditorUtility.OpenFolderPanel("Style proposals run", StyleProposals.Folder, "");
        if (string.IsNullOrEmpty(picked)) return;
        string root = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/') + "/";
        Load(picked.Replace('\\', '/').Replace(root, ""));
    }

    private static void Reveal(ProposalItem item)
    {
        if (item.inScene)
        {
            if (!string.IsNullOrEmpty(item.assetPath) && EditorSceneManager.GetSceneByPath(item.assetPath).isLoaded == false
                && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(item.assetPath);
            var scene = string.IsNullOrEmpty(item.assetPath) ? EditorSceneManager.GetActiveScene() : EditorSceneManager.GetSceneByPath(item.assetPath);
            if (!scene.isLoaded) return;
            string[] parts = item.indexPath.Split(new[] { '/' }, 2);
            GameObject[] roots = scene.GetRootGameObjects();
            if (int.TryParse(parts[0], out int r) && r < roots.Length)
                Select(ButtonScanner.FindByIndexPath(roots[r].transform, parts.Length > 1 ? parts[1] : ""));
            return;
        }
        PrefabStage stage = PrefabStageUtility.OpenPrefab(item.assetPath);
        if (stage != null) Select(ButtonScanner.FindByIndexPath(stage.prefabContentsRoot.transform, item.indexPath));
    }

    private static void Select(Transform t)
    {
        if (t == null) return;
        Selection.activeGameObject = t.gameObject;
        EditorGUIUtility.PingObject(t.gameObject);
    }
}
