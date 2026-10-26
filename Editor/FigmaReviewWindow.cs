using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Figma sync's result as swatches instead of hex codes: every mapped component with Figma and Unity side by side,
/// row by row, and the decisions the sync leaves to a developer — confirm a suggested mapping, mark a component as not
/// used in the game, or keep a value under an agreed platform rule. Opening it writes nothing; each decision writes
/// only component-map.json or adaptations.json and regenerates the doc. A value that should follow Figma is changed
/// through Style Proposals.
/// </summary>
public class FigmaReviewWindow : EditorWindow
{
    private const string Title = "Figma Review";
    private static readonly string[] Filters = { "To do", "All" };
    private const float FieldWidth = 170f, ValueWidth = 92f, UnityValueWidth = 130f, SwatchSize = 16f, ResultWidth = 190f;

    [MenuItem(DesignSystemMenu.Root + "Figma/Review…", priority = 29)]
    public static void Open()
    {
        var window = GetWindow<FigmaReviewWindow>(Title);
        window.minSize = new Vector2(720f, 420f);
        window.Reload();
    }

    private FigmaReport report;
    private List<FigmaPlatformRule> rules = new List<FigmaPlatformRule>();
    private Dictionary<string, UIStateStyle> stateStyles = new Dictionary<string, UIStateStyle>();
    private List<IGrouping<string, FigmaFieldRow>> groups = new List<IGrouping<string, FigmaFieldRow>>();
    private string error;
    private int filter;
    private Vector2 scroll;
    private GUIStyle wrap, small;
    private readonly HashSet<string> folded = new HashSet<string>();
    private readonly Dictionary<string, int> chosenRule = new Dictionary<string, int>();
    private const int MaxCaptures = 6;
    private const float PictureHeight = 90f, PictureMaxWidth = 260f;
    /// <summary>Uses by style name; null until a panel's Used in list is first opened.</summary>
    private Dictionary<string, List<StyleUse>> uses;
    private readonly HashSet<string> usesOpen = new HashSet<string>();
    private readonly Dictionary<StyleUse, Texture2D> captures = new Dictionary<StyleUse, Texture2D>();
    private readonly Dictionary<string, Texture2D> figmaImages = new Dictionary<string, Texture2D>();

    private void OnEnable() => Reload();

    private void OnDisable() => ClearPictures();

    private void ClearPictures()
    {
        foreach (Texture2D t in captures.Values.Concat(figmaImages.Values)) if (t != null) DestroyImmediate(t);
        captures.Clear();
        figmaImages.Clear();
        uses = null;
    }

    private void Reload()
    {
        ClearPictures();
        try { Display(FigmaSync.Preview()); error = null; }
        catch (Exception e) { report = null; error = e.Message; }
    }

    /// <summary>Runs one decision, then compares again and rewrites the doc so it never lags the files.</summary>
    private void Decide(Action decision)
    {
        try { decision(); Display(FigmaSync.Run()); error = null; }
        catch (Exception e) { Reload(); error = e.Message; }
        GUIUtility.ExitGUI();
    }

    private void Display(FigmaReport compared)
    {
        report = compared;
        rules = FigmaSync.Load<List<FigmaPlatformRule>>(FigmaSync.RulesPath) ?? new List<FigmaPlatformRule>();
        stateStyles = DesignSystemAssets.FindAll<UIStateStyle>().Where(s => s != null).GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First());
        groups = report?.rows.GroupBy(r => r.style).ToList() ?? new List<IGrouping<string, FigmaFieldRow>>();
        Repaint();
    }

    private void OnGUI()
    {
        wrap ??= new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };
        small ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = true };
        if (report == null)
        {
            EditorGUILayout.HelpBox(error ?? "No Figma snapshot yet. The sync owner fetches one first (the figma-sync skill).", MessageType.Info);
            if (GUILayout.Button("Reload")) Reload();
            return;
        }

        DrawHeader();
        if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (IGrouping<string, FigmaFieldRow> group in groups.Where(g => filter == 1 || NeedsWork(g)))
            DrawGroup(group);
        if (filter == 0 && groups.Count > 0 && groups.All(g => !NeedsWork(g)))
            EditorGUILayout.HelpBox("Every mapped value matches Figma or is kept under an agreed rule.", MessageType.None);
        DrawSuggestions();
        DrawUnmapped();
        DrawUnityOnly();
        DrawRules();
        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"<b>{report.snapshot.fileName}</b> · version {report.snapshot.figmaVersion} · read {report.snapshot.syncedAt}" +
                                       (report.hasBaseline ? " · compared with the last accepted sync" : " · <b>first sync</b>, no baseline"), wrap);
            if (GUILayout.Button("Reload", EditorStyles.miniButton, GUILayout.Width(60))) Reload();
            if (GUILayout.Button("Write doc", EditorStyles.miniButton, GUILayout.Width(70))) Decide(() => { });
            if (GUILayout.Button("Open doc", EditorStyles.miniButton, GUILayout.Width(70)))
                EditorUtility.OpenWithDefaultApp(Path.GetFullPath($"{DesignSystemDoc.Folder}/{DesignSystemDoc.FileName}"));
        }
        EditorGUILayout.LabelField(FigmaSync.Summary(report.rows) + $". Figma wins: a difference is kept only under an agreed platform rule. " +
                                   "Swatches are drawn over white; a state drawn at reduced opacity also shows how it looks drawn.", small);
        filter = GUILayout.Toolbar(filter, Filters, EditorStyles.toolbarButton);
        EditorGUILayout.Space(4f);
    }

    private static bool NeedsWork(IEnumerable<FigmaFieldRow> rows) => rows.Any(r => !r.mappingConfirmed || NeedsWork(r));

    private static bool NeedsWork(FigmaFieldRow r) => r.result != FigmaResult.InSync && r.result != FigmaResult.Adapted;

    private void DrawGroup(IGrouping<string, FigmaFieldRow> group)
    {
        FigmaFieldRow first = group.First();
        string component = first.figmaComponent.Split(new[] { " · " }, StringSplitOptions.None)[0];
        int todo = group.Count(NeedsWork);
        string status = first.mappingConfirmed ? "<color=#3A9D5D>confirmed</color>" : "<color=#D98A1C>suggested, not confirmed</color>";

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool open = EditorGUILayout.Foldout(!folded.Contains(group.Key), $"{component}  →  {group.Key}", true, EditorStyles.foldoutHeader);
                if (open) folded.Remove(group.Key); else folded.Add(group.Key);
                GUILayout.Label($"{status} · {(todo == 0 ? "nothing to do" : $"{todo} to do")}", wrap, GUILayout.Width(250));
                if (!first.mappingConfirmed)
                {
                    if (GUILayout.Button("Confirm mapping", EditorStyles.miniButton, GUILayout.Width(110)))
                        Decide(() => FigmaSync.Confirm(first.figmaId, null, Environment.UserName));
                    if (GUILayout.Button("Not used in game", EditorStyles.miniButton, GUILayout.Width(110)))
                        Decide(() => FigmaSync.Unmap(first.figmaId, Environment.UserName));
                }
            }
            if (folded.Contains(group.Key)) return;

            List<FigmaFieldRow> shown = group.Where(r => filter == 1 || NeedsWork(r)).ToList();
            if (shown.Count > 0) DrawColumnHeads();
            foreach (FigmaFieldRow r in shown) DrawRow(r);
            if (shown.Count == 0) EditorGUILayout.LabelField("All rows match. Switch to All to see them.", small);

            List<FigmaStateGap> gaps = report.stateGaps.Where(g => g.style == group.Key).ToList();
            if (gaps.Count > 0)
                EditorGUILayout.LabelField("Not compared, only one side has the state: " +
                                           string.Join(", ", gaps.Select(g => $"{g.state} (only in {g.onlyIn})")), small);
            DrawFigmaPictures(shown.Count > 0 ? shown : group.ToList());
            DrawUses(group.Key);
        }
    }

    private void DrawFigmaPictures(List<FigmaFieldRow> rows)
    {
        var variants = rows.Where(r => r.figmaVariantId != null).GroupBy(r => r.figmaVariantId)
                           .Select(g => (image: FigmaImage(g.Key), label: VariantLabel(g.First().figmaComponent))).ToList();
        if (variants.Count == 0) return;
        EditorGUILayout.LabelField("In Figma", EditorStyles.miniBoldLabel);
        if (variants.All(v => v.image == null))
            EditorGUILayout.LabelField("No Figma pictures yet. The sync owner runs: python Tools/figma_sync.py images", small);
        else Strip(variants);
    }

    private static string VariantLabel(string figmaComponent)
    {
        int dot = figmaComponent.IndexOf(" · ", StringComparison.Ordinal);
        return dot < 0 ? figmaComponent : figmaComponent.Substring(dot + 3);
    }

    private Texture2D FigmaImage(string variantId)
    {
        if (figmaImages.TryGetValue(variantId, out Texture2D cached)) return cached;
        string path = FigmaSync.ImagePath(variantId);
        Texture2D image = null;
        if (File.Exists(path))
        {
            image = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            image.LoadImage(File.ReadAllBytes(path));
        }
        return figmaImages[variantId] = image;
    }

    private void DrawUses(string style)
    {
        bool open = usesOpen.Contains(style);
        string count = uses == null ? "" : $" ({UsesOf(style).Count})";
        if (EditorGUILayout.Foldout(open, $"Used in the game{count}", true) != open)
        {
            if (open) usesOpen.Remove(style); else usesOpen.Add(style);
            if (!open && uses == null) Later(FindUses);
        }
        if (!usesOpen.Contains(style) || uses == null) return;

        List<StyleUse> list = UsesOf(style);
        if (list.Count == 0) EditorGUILayout.LabelField("No prefab or scene references this style.", small);
        foreach (StyleUse use in list)
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{Path.GetFileName(use.assetPath)} › {use.objectPath}" +
                                           (use.inScene ? " <color=#888888>(scene, read as text)</color>" : ""), wrap);
                if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44)))
                    EditorGUIUtility.PingObject(use.control != null ? use.control.gameObject : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(use.assetPath));
                if (use.control != null && GUILayout.Button("Open prefab", EditorStyles.miniButton, GUILayout.Width(80))) OpenInPrefab(use);
            }

        List<StyleUse> capturable = list.Where(u => u.control != null).Take(MaxCaptures).ToList();
        if (capturable.Count == 0) return;
        if (capturable.All(captures.ContainsKey))
            Strip(capturable.Select(u => (captures[u], u.objectPath.Substring(u.objectPath.LastIndexOf('/') + 1))).ToList());
        else if (GUILayout.Button(new GUIContent($"Capture controls ({capturable.Count})",
                     "Renders a hidden copy of each control's screen, as saved in the prefab; nothing is saved."), EditorStyles.miniButton, GUILayout.Width(150)))
            Later(() => Capture(capturable));
    }

    /// <summary>Runs work that changes what the window draws, or renders, after the current GUI event.</summary>
    private void Later(Action work) => EditorApplication.delayCall += () =>
    {
        if (this == null) return;
        try { work(); }
        catch (Exception e) { error = e.Message; }
        Repaint();
    };

    private List<StyleUse> UsesOf(string style) =>
        uses != null && uses.TryGetValue(style, out List<StyleUse> list) ? list : new List<StyleUse>();

    private void FindUses()
    {
        var styles = new List<UnityEngine.Object>();
        foreach (string name in groups.Select(g => g.Key))
        {
            if (stateStyles.TryGetValue(name, out UIStateStyle state)) styles.Add(state);
            else if (DesignSystemAssets.FindAll<UIComponentStyle>().FirstOrDefault(s => s != null && s.name == name) is UIComponentStyle component)
                styles.Add(component);
        }
        uses = StyleUses.Find(styles).ToDictionary(kv => kv.Key.name, kv => kv.Value);
    }

    private void Capture(IEnumerable<StyleUse> list)
    {
        using var rig = new CaptureRig();
        foreach (StyleUse use in list)
        {
            CaptureRig.Shot shot = rig.Take(use.control, null, null);
            Texture2D image = null;
            if (shot?.Png != null)
            {
                image = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                image.LoadImage(shot.Png);
            }
            captures[use] = image;
        }
    }

    private static void OpenInPrefab(StyleUse use)
    {
        AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(use.assetPath));
        Transform root = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage()?.prefabContentsRoot.transform;
        if (root == null) return;
        int slash = use.objectPath.IndexOf('/');
        Transform target = slash < 0 ? root : root.Find(use.objectPath.Substring(slash + 1));
        if (target == null) return;
        Selection.activeTransform = target;
        EditorGUIUtility.PingObject(target.gameObject);
    }

    /// <summary>Pictures in a row that wraps to the window's width, each scaled to one height with its label under it.</summary>
    private void Strip(List<(Texture2D image, string label)> pictures)
    {
        float room = position.width - 40f, used = 0f;
        EditorGUILayout.BeginHorizontal();
        foreach ((Texture2D image, string label) in pictures)
        {
            float width = image == null ? 120f : Mathf.Min(PictureMaxWidth, PictureHeight * image.width / Mathf.Max(1, image.height));
            float height = image == null ? 24f : Mathf.Min(PictureHeight, width * image.height / Mathf.Max(1, image.width));
            if (used > 0f && used + width > room)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                used = 0f;
            }
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(width)))
            {
                Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
                if (image != null) GUI.DrawTexture(rect, image, ScaleMode.ScaleToFit);
                else EditorGUI.LabelField(rect, "no picture", small);
                GUILayout.Label(label, small, GUILayout.Width(width));
            }
            used += width + 6f;
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawColumnHeads()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Field", EditorStyles.miniBoldLabel, GUILayout.Width(FieldWidth));
            GUILayout.Label("Figma", EditorStyles.miniBoldLabel, GUILayout.Width(SwatchSize + ValueWidth + 4f));
            GUILayout.Label("Unity", EditorStyles.miniBoldLabel, GUILayout.Width(2f * SwatchSize + UnityValueWidth + 8f));
            GUILayout.Label("Result", EditorStyles.miniBoldLabel, GUILayout.Width(ResultWidth));
            GUILayout.Label("Decide", EditorStyles.miniBoldLabel);
        }
    }

    private void DrawRow(FigmaFieldRow r)
    {
        float? opacity = DrawnOpacity(r);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(DesignSystemDoc.FieldName(r.field), GUILayout.Width(FieldWidth));
            Swatch(r.figmaNow, 1f);
            GUILayout.Label(r.figmaNow ?? "—", GUILayout.Width(ValueWidth));
            Swatch(r.unityNow, 1f);
            if (opacity.HasValue) Swatch(r.unityNow, opacity.Value); else GUILayout.Space(SwatchSize + 4f);
            GUILayout.Label(r.unityNow == null ? "component's own" : opacity.HasValue ? $"{r.unityNow} at {opacity.Value:P0}" : r.unityNow,
                            GUILayout.Width(UnityValueWidth));
            GUILayout.Label(ResultLabel(r.result), wrap, GUILayout.Width(ResultWidth));
            DrawDecision(r);
        }
        if (r.markProblem != null) EditorGUILayout.LabelField($"Marked, but {Lower(r.markProblem)}", small);
        if (r.result == FigmaResult.DesignUpdate || r.result == FigmaResult.Conflict || r.result == FigmaResult.AdaptedFigmaMoved)
            EditorGUILayout.LabelField($"Figma at the last accepted sync: {r.figmaBaseline ?? "—"}", small);
    }

    private void DrawDecision(FigmaFieldRow r)
    {
        if (r.result == FigmaResult.InSync) return;
        if (r.adaptation != null && r.markProblem == null)
        {
            GUILayout.Label($"kept: <b>{r.adaptation.rule}</b>", wrap, GUILayout.Width(130));
            if (GUILayout.Button("Unmark", EditorStyles.miniButton, GUILayout.Width(60))) Decide(() => FigmaSync.Unmark(r.style, r.field));
            return;
        }

        List<FigmaPlatformRule> covering = FigmaSync.RulesCovering(r.field, rules);
        if (covering.Count > 0)
        {
            string key = $"{r.style}|{r.field}";
            chosenRule.TryGetValue(key, out int choice);
            choice = Mathf.Clamp(EditorGUILayout.Popup(choice, covering.Select(x => x.id).ToArray(), GUILayout.Width(110)), 0, covering.Count - 1);
            chosenRule[key] = choice;
            if (GUILayout.Button(new GUIContent("Keep", covering[choice].rule), EditorStyles.miniButton, GUILayout.Width(44)))
                Decide(() => FigmaSync.MarkAdapted(r.style, r.field, covering[choice].id, null, Environment.UserName));
        }
        else GUILayout.Label(new GUIContent("no agreed rule", "No agreed platform rule covers this field, so the game follows Figma."), small, GUILayout.Width(80));
        if (GUILayout.Button(new GUIContent("Follow Figma…", "Change the style through Style Proposals."), EditorStyles.miniButton, GUILayout.Width(92)))
            StyleProposalsWindow.Open();
    }

    /// <summary>The opacity a state style draws this row's state at, when below 1.</summary>
    private float? DrawnOpacity(FigmaFieldRow r)
    {
        int dot = r.field.LastIndexOf('.');
        if (dot < 0 || !stateStyles.TryGetValue(r.style, out UIStateStyle style)) return null;
        UIStateStyle.State state = style.Find(r.field.Substring(0, dot));
        return state != null && state.opacity < 0.999f ? state.opacity : (float?)null;
    }

    private static void Swatch(string hex, float opacity)
    {
        Rect rect = GUILayoutUtility.GetRect(SwatchSize, SwatchSize, GUILayout.Width(SwatchSize), GUILayout.Height(SwatchSize));
        if (hex == null || !hex.StartsWith("#") || !ColorUtility.TryParseHtmlString(hex, out Color c))
        {
            GUILayout.Space(4f);
            return;
        }
        Color drawn = Color.Lerp(Color.white, new Color(c.r, c.g, c.b, 1f), c.a * opacity);
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.5f));
        EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), drawn);
        GUILayout.Space(4f);
    }

    private static string ResultLabel(FigmaResult result)
    {
        string colour = result switch
        {
            FigmaResult.InSync => "#3A9D5D",
            FigmaResult.Adapted => "#3A9D5D",
            FigmaResult.DesignUpdate => "#3B82C4",
            FigmaResult.AdaptedFigmaMoved => "#3B82C4",
            _ => "#D98A1C",
        };
        return $"<color={colour}>{FigmaSync.Label(result)}</color>";
    }

    private static string Lower(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

    private void DrawSuggestions()
    {
        List<FigmaMapEntry> suggested = report.unmapped.Where(u => !string.IsNullOrEmpty(u.unityStyle)).ToList();
        if (suggested.Count == 0) return;
        Section("Suggested, not compared yet", "The sync matched these by value or name. Confirm one to compare it, or say the game doesn't use it.");
        foreach (FigmaMapEntry s in suggested)
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{s.figmaName}  →  <b>{s.unityStyle}</b>", wrap);
                if (GUILayout.Button(new GUIContent("Confirm", s.note), EditorStyles.miniButton, GUILayout.Width(70)))
                    Decide(() => FigmaSync.Confirm(s.figmaId, null, Environment.UserName));
                if (GUILayout.Button("Not used in game", EditorStyles.miniButton, GUILayout.Width(110)))
                    Decide(() => FigmaSync.Unmap(s.figmaId, Environment.UserName));
            }
    }

    private void DrawUnmapped()
    {
        List<FigmaMapEntry> unmatched = report.unmapped.Where(u => string.IsNullOrEmpty(u.unityStyle)).ToList();
        if (unmatched.Count == 0 && report.notUsed.Count == 0) return;
        Section("Figma components with no Unity style", "Map one by hand in component-map.json if a style should follow it.");
        foreach (FigmaMapEntry u in unmatched)
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{u.figmaName} · <color=#888888>{u.note}</color>", wrap);
                if (GUILayout.Button("Not used in game", EditorStyles.miniButton, GUILayout.Width(110)))
                    Decide(() => FigmaSync.Unmap(u.figmaId, Environment.UserName));
            }
        foreach (FigmaMapEntry n in report.notUsed)
            EditorGUILayout.LabelField($"{n.figmaName} · <color=#888888>{n.note}</color>", wrap);
    }

    private void DrawUnityOnly()
    {
        if (report.unmappedStateStyles.Count == 0) return;
        Section("State styles no Figma component follows", "Unity only. Add the Figma section to sync-config.json if the designer has them.");
        foreach (string s in report.unmappedStateStyles) EditorGUILayout.LabelField(s);
    }

    private void DrawRules()
    {
        Section("Platform rules", "The only reasons the game may differ from Figma, agreed once with the designer in figma/rules.json.");
        if (rules.Count == 0) EditorGUILayout.LabelField("None yet, so the game follows Figma everywhere.", small);
        foreach (FigmaPlatformRule r in rules.Where(r => r != null))
            EditorGUILayout.LabelField($"<b>{r.id}</b>: {r.rule} <color=#888888>({(r.fields == null || r.fields.Count == 0 ? "any field" : string.Join(", ", r.fields))}; " +
                                       $"{(r.Agreed ? $"agreed by {r.agreedBy}, {r.date}" : "proposed: covers nothing until the designer agrees it")})</color>", wrap);
    }

    private void Section(string title, string hint)
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(hint, small);
    }
}
