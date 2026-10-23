using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Where a control and its neighbours sit on screen, so two captures can be compared for layout changes.</summary>
[Serializable]
public class RectRecord
{
    public string name;
    public float x, y, w, h;
}

/// <summary>What a proposal does to the layout around the control.</summary>
[Serializable]
public class LayoutImpact
{
    public bool changes;
    public string detail = "";
}

/// <summary>A style that could go on a control, with what it would look like and do to the layout.</summary>
[Serializable]
public class ProposalCandidate
{
    public string style;
    public string stylePath;
    /// <summary>Safe, Check or None, as <see cref="ButtonScanner.Match"/> judges it.</summary>
    public string confidence;
    public float rank;
    public string summary;
    public bool keepsStates;
    public string image;
    public LayoutImpact impact = new LayoutImpact();
}

/// <summary>A new style made for one special control: a variant of an existing style that keeps what makes it special.</summary>
[Serializable]
public class CustomStyleSpec
{
    public string baseStyle;
    public string name;
    /// <summary>Keep each control's own height and width.</summary>
    public bool keepSize;
    /// <summary>Keep each label's own font and size.</summary>
    public bool keepLabelFont;
    public float height;
    public float labelSize;
    public bool square;
    /// <summary>The height is a minimum and the control grows to fit its text.</summary>
    public bool growWithContent;
    /// <summary>A text role from the type scale for the labels, e.g. BodySmall. Empty = the base style's.</summary>
    public string labelRole = "";
}

/// <summary>Where one control lives, so it can be found again after its prefab or scene is reloaded.</summary>
[Serializable]
public class ProposalTarget
{
    public string assetPath;
    public bool inScene;
    public string indexPath;
    public string name;
    public string where;
}

/// <summary>One control under review: what it is, what could style it, what was decided and whether a person approved.</summary>
[Serializable]
public class ProposalItem
{
    public string id;
    public string assetPath;
    public bool inScene;
    public string indexPath;
    public string name;
    public string where;
    public string kind;
    /// <summary>The style the control has now, if any.</summary>
    public string currentStyle = "";
    /// <summary>Text put in the control's label for both captures, so a placeholder doesn't hide how real content fits.</summary>
    public string previewText = "";

    public string sprite;
    public bool spriteIsShape;
    public string fill;
    public string label;
    public string size;
    public string transition;
    public bool hiddenByDefault;
    /// <summary>How many controls in the scan look exactly like this one.</summary>
    public int sameLook;
    /// <summary>The other controls that look exactly like this one; a decision applies to all of them.</summary>
    public List<ProposalTarget> others = new List<ProposalTarget>();
    public string parentLayout;
    /// <summary>The styles its siblings already use, most common first.</summary>
    public List<string> siblingStyles = new List<string>();

    public string before;
    public List<ProposalCandidate> candidates = new List<ProposalCandidate>();

    /// <summary>apply, custom, unique or skip — written by whoever judges the item.</summary>
    public string decision = "";
    public string decisionStyle = "";
    /// <summary>Share the style's look but keep this control's own size, so nothing around it moves.</summary>
    public bool keepOwnSize;
    public CustomStyleSpec custom = new CustomStyleSpec();
    public string reason = "";
    public string proposalImage = "";
    public LayoutImpact proposalImpact = new LayoutImpact();

    /// <summary>pending, approved or rejected.</summary>
    public string review = "pending";
    public string reviewer = "";
    public string reviewNote = "";
    public bool acceptLayoutChange;
    public string applied = "";
}

[Serializable]
public class ProposalRun
{
    public string created;
    public string scope;
    public int scanned;
    public int alreadyStyled;
    public int safeMatches;
    public int pictures;
    public int unique;
    public int notCaptured;
    /// <summary>Looks that needed review but were past the item limit.</summary>
    public int notReviewed;
    public List<ProposalItem> items = new List<ProposalItem>();
}

/// <summary>
/// Finds controls whose look doesn't follow the design system and prepares proposals a person approves before
/// anything changes. <see cref="Audit"/> scans, ranks each style that could fit a control, and captures the control
/// in its real layout before and after each candidate, measuring whether anything around it moves. A judge — the
/// style-review skill, or a person — fills in each item's decision; <see cref="Prepare"/> renders those decisions;
/// the Style Proposals window approves them; <see cref="ApplyApproved"/> is the only step that edits prefabs and scenes.
/// </summary>
public static class StyleProposals
{
    public const string Folder = "Artifacts/StyleReview";
    private const int MaxCandidates = 3;

    // ---- Audit ----

    /// <summary>
    /// Scope: "selection", "open" (open scenes and prefab stage), "project", or an asset path — a prefab, scene or
    /// folder. Returns the run folder holding review.json and the captures.
    /// </summary>
    public static string Audit(string scope, int maxItems = 40)
    {
        EnsureEditing();
        DesignSystemSettings settings = DesignSystemSettings.GetOrCreate();
        ScanResult scan = ScanScope(scope, settings);
        List<UIComponentStyle> styles = DesignSystemAssets.FindAll<UIComponentStyle>();
        ButtonScanner.Suggest(scan.Groups, styles, settings.matchTolerance);

        string folder = Path.Combine(Folder, DateTime.Now.ToString("yyyyMMdd-HHmmss")).Replace('\\', '/');
        Directory.CreateDirectory(folder);
        var run = new ProposalRun
        {
            created = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            scope = scope,
            scanned = scan.Groups.Sum(g => g.Count) + scan.Styled,
            alreadyStyled = scan.Styled,
            safeMatches = scan.CountWhere(MatchConfidence.Safe),
            pictures = scan.Pictures,
            unique = scan.Unique,
        };

        // Panels aren't controls a player presses; the Check tab covers them.
        List<LookGroup> needed = scan.Groups.Where(g => g.Confidence != MatchConfidence.Safe && g.HasBackground && !g.IsPicture
                                                        && g.Kind != ComponentKinds.Panels).OrderByDescending(g => g.Count).ToList();
        foreach (LookGroup g in needed.Take(maxItems)) run.items.Add(Describe(g, styles, settings.matchTolerance, run.items.Count));
        run.notReviewed = Math.Max(0, needed.Count - maxItems);

        CaptureAll(run, folder, mutateFor: item => item.candidates.Select(c => (c.image = $"{item.id}-{Slug(c.style)}.png",
            (Action<Transform>)(t => Restyle(t, AssetDatabase.LoadAssetAtPath<UIComponentStyle>(c.stylePath))), c.impact)));
        Save(run, folder);
        return folder;
    }

    private static ScanResult ScanScope(string scope, DesignSystemSettings settings)
    {
        switch (scope)
        {
            case "selection":
                return ButtonScanner.ScanUnder(Selection.gameObjects, includePrefabSources: true, settings);
            case "open":
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                IEnumerable<GameObject> roots = stage != null
                    ? new[] { stage.prefabContentsRoot }
                    : Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => s.isLoaded).SelectMany(s => s.GetRootGameObjects());
                return ButtonScanner.ScanUnder(roots, includePrefabSources: true, settings);
            case "project":
                return ButtonScanner.Scan(settings);
            default:
                DesignSystemSettings scoped = Object.Instantiate(settings);
                try
                {
                    scoped.includedPaths = new List<string> { scope.TrimEnd('/') };
                    scoped.scanPrefabs = !scope.EndsWith(".unity");
                    scoped.sceneScope = DesignSystemSettings.SceneScope.AllScenes;
                    return ButtonScanner.Scan(scoped);
                }
                finally
                {
                    Object.DestroyImmediate(scoped);
                }
        }
    }

    private static ProposalItem Describe(LookGroup g, List<UIComponentStyle> styles, float tolerance, int index)
    {
        ProposalTarget first = Target(g.Buttons[0]);
        var item = new ProposalItem
        {
            id = $"c{index + 1:00}",
            assetPath = first.assetPath,
            inScene = first.inScene,
            indexPath = first.indexPath,
            name = first.name,
            where = first.where,
            others = g.Buttons.Skip(1).Select(Target).ToList(),
            kind = g.KindName,
            sprite = g.Sprite != null ? g.Sprite.name : "none",
            spriteIsShape = g.Sprite == null || SurfaceLook.IsShape(g.Sprite),
            fill = "#" + ColorUtility.ToHtmlStringRGBA(g.Visible),
            label = g.HasLabel ? "#" + ColorUtility.ToHtmlStringRGBA(g.Label) : "none",
            transition = g.Transition.ToString(),
            sameLook = g.Count,
        };
        item.candidates = styles
            .Where(s => ButtonScanner.CanTake(g, s))
            .Select(s => (style: s, confidence: ButtonScanner.Match(g, s, tolerance, out float rank), rank))
            .OrderByDescending(m => m.confidence).ThenBy(m => m.rank).Take(MaxCandidates)
            .Select(m => new ProposalCandidate
            {
                style = m.style.name,
                stylePath = AssetDatabase.GetAssetPath(m.style),
                confidence = m.confidence.ToString(),
                rank = (float)Math.Round(m.rank, 3),
                summary = Summary(m.style),
                keepsStates = ButtonScanner.KeepsStates(m.style, g, tolerance),
            }).ToList();
        return item;
    }

    private static ProposalTarget Target(ButtonRef b)
    {
        var target = new ProposalTarget { name = b.Name, where = b.Display, assetPath = b.AssetPath, inScene = b.InScene, indexPath = b.IndexPath };
        if (b.Live == null) return target;
        Transform root = b.Live.transform.root;
        string rest = ButtonScanner.IndexPath(root, b.Live.transform);
        target.assetPath = b.Live.gameObject.scene.path;
        target.inScene = true;
        target.indexPath = rest.Length == 0 ? root.GetSiblingIndex().ToString() : $"{root.GetSiblingIndex()}/{rest}";
        return target;
    }

    /// <summary>The control that was captured, then every look-alike.</summary>
    public static IEnumerable<ProposalTarget> Targets(ProposalItem item) =>
        new[] { new ProposalTarget { assetPath = item.assetPath, inScene = item.inScene, indexPath = item.indexPath, name = item.name, where = item.where } }
            .Concat(item.others ?? new List<ProposalTarget>());

    public static string Summary(UIComponentStyle style) =>
        $"{DesignSystemDescribe.Height(style)} · {DesignSystemDescribe.LabelFont(style)}" + (style.IsVariant ? $" · variant of {style.BaseStyle.name}" : "");

    // ---- Proposing one change for a kind of control ----

    /// <summary>
    /// Proposes one decision — usually a style — for every control carrying the named component in the scope's
    /// prefabs: one item per prefab, since a prefab's copies of a control look alike. With currentStyle, only
    /// controls styled with it now. Captures each before and after, with the preview text in its label when given,
    /// and returns the run folder for review.
    /// </summary>
    public static string ProposeFor(string componentType, string scope, ProposalItem decision, string currentStyle = null)
    {
        EnsureEditing();
        string folder = Path.Combine(Folder, DateTime.Now.ToString("yyyyMMdd-HHmmss")).Replace('\\', '/');
        Directory.CreateDirectory(folder);
        var run = new ProposalRun { created = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), scope = $"{componentType} in {scope}" };

        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { scope }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            List<Component> controls = prefab.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name == componentType && !c.TryGetComponent(out UniqueDesign _)
                            && OwnsLook(c) && (currentStyle == null || StyleName(c) == currentStyle)).ToList();
            if (controls.Count == 0) continue;
            List<ProposalTarget> targets = controls.Select(c => new ProposalTarget
            {
                assetPath = path,
                indexPath = ButtonScanner.IndexPath(prefab.transform, c.transform),
                name = c.name,
                where = $"{path} › {ButtonScanner.HierarchyPath(c.transform)}",
            }).ToList();
            StyledElement styled = controls[0].GetComponent<StyledElement>();
            run.items.Add(new ProposalItem
            {
                id = $"c{run.items.Count + 1:00}",
                assetPath = path,
                indexPath = targets[0].indexPath,
                name = targets[0].name,
                where = targets[0].where,
                others = targets.Skip(1).ToList(),
                sameLook = targets.Count,
                kind = componentType,
                currentStyle = styled != null && styled.ComponentStyle != null ? styled.ComponentStyle.name : "",
                previewText = decision.previewText,
                decision = decision.decision,
                decisionStyle = decision.decisionStyle,
                keepOwnSize = decision.keepOwnSize,
                custom = decision.custom,
                reason = decision.reason,
            });
        }
        CaptureAll(run, folder, mutateFor: _ => Enumerable.Empty<(string, Action<Transform>, LayoutImpact)>());
        Save(run, folder);
        Prepare(folder);
        return folder;
    }

    // A control's look belongs to the prefab that holds its styled component — often an outer prefab that styled a
    // nested instance itself. Unstyled, it belongs to the prefab that defines the control. Proposing on the other
    // one would miss the style, or give the instances a second styled component.
    private static bool OwnsLook(Component c) =>
        c.TryGetComponent(out StyledElement styled)
            ? PrefabUtility.GetCorrespondingObjectFromSource(styled) == null
            : PrefabUtility.GetCorrespondingObjectFromSource(c) == null;

    private static string StyleName(Component c) =>
        c.TryGetComponent(out StyledElement styled) && styled.ComponentStyle != null ? styled.ComponentStyle.name : null;

    // ---- Prepare ----

    /// <summary>
    /// Renders each judged item's proposal — the chosen style, or the custom variant it describes — with its layout
    /// impact, and resets its review. Items judged unique or skip need no render.
    /// </summary>
    public static void Prepare(string folder)
    {
        EnsureEditing();
        ProposalRun run = Load(folder);
        var custom = new Dictionary<string, UIComponentStyle>();
        try
        {
            CaptureAll(run, folder, mutateFor: item =>
            {
                if (item.decision != "apply" && item.decision != "custom") return Enumerable.Empty<(string, Action<Transform>, LayoutImpact)>();
                UIComponentStyle style = ProposedStyle(item, custom);
                if (style == null) return Enumerable.Empty<(string, Action<Transform>, LayoutImpact)>();
                item.proposalImage = $"{item.id}-proposal.png";
                item.proposalImpact = new LayoutImpact();
                return new[] { (item.proposalImage, (Action<Transform>)(t => Restyle(t, style, keepOwnSize: item.keepOwnSize)), item.proposalImpact) };
            }, captureBefore: false);
        }
        finally
        {
            foreach (UIComponentStyle s in custom.Values) Object.DestroyImmediate(s);
        }
        foreach (ProposalItem item in run.items.Where(i => string.IsNullOrEmpty(i.applied)))
        {
            item.review = "pending";
            item.acceptLayoutChange = false;
        }
        Save(run, folder);
    }

    /// <summary>The style an item's decision puts on it; a custom one is built in memory and cached by name.</summary>
    public static UIComponentStyle ProposedStyle(ProposalItem item, Dictionary<string, UIComponentStyle> custom)
    {
        if (item.decision == "apply") return FindStyle(item.decisionStyle);
        if (item.decision != "custom" || item.custom == null) return null;
        if (custom.TryGetValue(item.custom.name ?? "", out UIComponentStyle made)) return made;
        UIComponentStyle baseStyle = FindStyle(item.custom.baseStyle);
        if (baseStyle == null) return null;
        made = BuildCustom(baseStyle, item.custom);
        custom[item.custom.name ?? ""] = made;
        return made;
    }

    private static UIComponentStyle FindStyle(string nameOrPath)
    {
        if (string.IsNullOrEmpty(nameOrPath)) return null;
        if (nameOrPath.EndsWith(".asset")) return AssetDatabase.LoadAssetAtPath<UIComponentStyle>(nameOrPath);
        return DesignSystemAssets.FindAll<UIComponentStyle>().FirstOrDefault(s => s.name == nameOrPath);
    }

    /// <summary>
    /// A variant of the base that keeps what the spec says is special about the control. In memory: the asset is
    /// only created when a person approves it.
    /// </summary>
    public static UIComponentStyle BuildCustom(UIComponentStyle baseStyle, CustomStyleSpec spec)
    {
        UIComponentStyle v = Object.Instantiate(baseStyle);
        v.name = string.IsNullOrEmpty(spec.name) ? baseStyle.name + "_Special" : spec.name;
        v.BaseStyle = baseStyle;
        v.Overrides.Clear();
        void Override(params string[] fields) { foreach (string f in fields) if (!v.Overrides.Contains(f)) v.Overrides.Add(f); }

        if (spec.keepSize)
        {
            v.HeightSource = ButtonHeightSource.KeepAuthored;
            v.Width = 0f;
            v.MinWidth = 0f;
            v.Square = false;
            Override("heightSource", "height", "size", "width", "minWidth", "square");
        }
        if (spec.height > 0f)
        {
            v.Height = spec.height;
            Override("heightSource", "height", "size");
        }
        if (spec.square)
        {
            v.Square = true;
            Override("square");
        }
        if (spec.keepLabelFont)
        {
            v.LabelFontSource = LabelFontSource.KeepAuthored;
            Override("labelFontSource", "labelRole", "labelSize");
        }
        if (spec.labelSize > 0f)
        {
            v.LabelSize = spec.labelSize;
            Override("labelSize");
        }
        if (spec.growWithContent)
        {
            v.GrowWithContent = true;
            Override("growWithContent");
        }
        if (!string.IsNullOrEmpty(spec.labelRole))
        {
            // A size of the style's own would outrank the role's.
            v.SetCustomLabelRole(spec.labelRole);
            v.LabelSize = 0f;
            Override("labelFontSource", "labelRole", "labelSize");
        }
        return v;
    }

    // ---- Capture ----

    /// <summary>
    /// Captures every item in its own layout, scene by scene and prefab by prefab: the before image (unless asked
    /// not to) and one image per mutation the caller lists, each from a fresh copy so mutations never stack.
    /// </summary>
    private static void CaptureAll(ProposalRun run, string folder,
        Func<ProposalItem, IEnumerable<(string file, Action<Transform> mutate, LayoutImpact impact)>> mutateFor, bool captureBefore = true)
    {
        using var rig = new CaptureRig();
        var byAsset = run.items.GroupBy(i => (i.assetPath, i.inScene)).ToList();
        for (int a = 0; a < byAsset.Count; a++)
        {
            var asset = byAsset[a];
            EditorUtility.DisplayProgressBar("Capturing controls", asset.Key.assetPath, (float)a / byAsset.Count);
            if (asset.Key.inScene)
                ButtonScanner.WithScene(asset.Key.assetPath, (scene, _) =>
                {
                    GameObject[] roots = scene.GetRootGameObjects();
                    foreach (ProposalItem item in asset)
                        CaptureItem(rig, path => FindInScene(roots, path), item, folder, mutateFor(item).ToList(), captureBefore, run);
                    return false;
                });
            else
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset.Key.assetPath);
                foreach (ProposalItem item in asset)
                    CaptureItem(rig, path => prefab != null ? ButtonScanner.FindByIndexPath(prefab.transform, path) : null,
                                item, folder, mutateFor(item).ToList(), captureBefore, run);
            }
        }
        EditorUtility.ClearProgressBar();
    }

    private static void CaptureItem(CaptureRig rig, Func<string, Transform> find, ProposalItem item, string folder,
        List<(string file, Action<Transform> mutate, LayoutImpact impact)> shots, bool captureBefore, ProposalRun run)
    {
        Transform source = find(item.indexPath);
        // The decision applies to every look-alike, so those on the same screen change in the preview too.
        List<Transform> lookAlikes = (item.others ?? new List<ProposalTarget>())
            .Where(o => o.assetPath == item.assetPath && o.inScene == item.inScene).Select(o => find(o.indexPath))
            .Where(t => t != null).ToList();
        if (source == null || source.name != item.name)
        {
            run.notCaptured++;
            return;
        }
        Action<Transform> withText = string.IsNullOrEmpty(item.previewText) ? null : (Action<Transform>)(t => SetLabel(t, item.previewText));
        CaptureRig.Shot before = rig.Take(source, withText, null);
        if (before == null)
        {
            run.notCaptured++;
            return;
        }
        item.hiddenByDefault = before.Hidden;
        item.size = $"{before.Target.w:0}×{before.Target.h:0}";
        item.parentLayout = before.ParentLayout;
        item.siblingStyles = before.SiblingStyles;
        if (captureBefore)
        {
            item.before = $"{item.id}-before.png";
            File.WriteAllBytes(Path.Combine(folder, item.before), before.Png);
        }
        foreach (var (file, mutate, impact) in shots)
        {
            CaptureRig.Shot after = rig.Take(source, t => { withText?.Invoke(t); mutate(t); }, before.Crop, lookAlikes, mutate);
            if (after == null) continue;
            File.WriteAllBytes(Path.Combine(folder, file), after.Png);
            LayoutImpact measured = CompareLayout(before.Rects, after.Rects);
            impact.changes = measured.changes;
            impact.detail = measured.detail;
        }
    }

    private static void SetLabel(Transform control, string text)
    {
        TMPro.TMP_Text label = control.TryGetComponent(out StyledElement styled) && styled.Label != null
            ? styled.Label
            : control.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (label != null) label.text = text;
    }

    private static Transform FindInScene(GameObject[] roots, string indexPath)
    {
        if (string.IsNullOrEmpty(indexPath)) return null;
        string[] parts = indexPath.Split(new[] { '/' }, 2);
        if (!int.TryParse(parts[0], out int r) || r >= roots.Length) return null;
        return ButtonScanner.FindByIndexPath(roots[r].transform, parts.Length > 1 ? parts[1] : "");
    }

    /// <summary>What moved or resized between two captures of the same control and its neighbours.</summary>
    public static LayoutImpact CompareLayout(List<RectRecord> before, List<RectRecord> after)
    {
        var notes = new List<string>();
        RectRecord target0 = before.FirstOrDefault(), target1 = after.FirstOrDefault();
        if (target0 != null && target1 != null && (Differs(target0.w, target1.w) || Differs(target0.h, target1.h)))
            notes.Add($"the control goes from {target0.w:0}×{target0.h:0} to {target1.w:0}×{target1.h:0}");
        int moved = before.Skip(1).Count(b =>
        {
            RectRecord a = after.FirstOrDefault(x => x.name == b.name);
            return a != null && (Differs(a.x, b.x) || Differs(a.y, b.y) || Differs(a.w, b.w) || Differs(a.h, b.h));
        });
        if (moved > 0) notes.Add($"{moved} neighbour(s) move or resize");
        return new LayoutImpact { changes = notes.Count > 0, detail = notes.Count > 0 ? string.Join("; ", notes) : "layout unchanged" };
    }

    private static bool Differs(float a, float b) => Mathf.Abs(a - b) > 0.5f;

    // ---- Applying ----

    /// <summary>
    /// Puts the style on a control, replacing the one it has. With keepOwnSize the control keeps its own size: the
    /// switch is set before the style first applies, so its size is never touched.
    /// </summary>
    public static void Restyle(Transform t, UIComponentStyle style, bool recordUndo = false, bool keepOwnSize = false)
    {
        if (t == null || style == null) return;
        Component control = t.GetComponents<Component>().FirstOrDefault(c => ComponentKinds.Fits(style, c));
        if (control == null) return;
        if (keepOwnSize)
        {
            Type styledType = ComponentKinds.For(style).StyledType;
            Component styled = t.GetComponent(styledType)
                                ?? (recordUndo ? Undo.AddComponent(t.gameObject, styledType) : t.gameObject.AddComponent(styledType));
            var so = new SerializedObject(styled);
            so.FindProperty("keepSize").boolValue = true;
            if (recordUndo) so.ApplyModifiedProperties();
            else so.ApplyModifiedPropertiesWithoutUndo();
        }
        DesignSystemAssets.Attach(control, style, recordUndo);
    }

    /// <summary>
    /// Carries out every approved, not yet applied item: puts its style on it (creating an approved custom style
    /// first), or marks it <see cref="UniqueDesign"/>. An item whose proposal changes the layout is only applied
    /// when that change was accepted too. Returns how many controls changed.
    /// </summary>
    public static int ApplyApproved(string folder)
    {
        EnsureEditing();
        ProposalRun run = Load(folder);
        List<ProposalItem> ready = run.items.Where(Ready).ToList();
        var created = new Dictionary<string, UIComponentStyle>();
        int changed = 0;

        var work = ready.SelectMany(item => Targets(item).Select(target => (item, target))).ToList();
        foreach (var asset in work.GroupBy(w => (w.target.assetPath, w.target.inScene)))
        {
            if (asset.Key.inScene)
                ButtonScanner.WithScene(asset.Key.assetPath, (scene, wasOpen) =>
                {
                    GameObject[] roots = scene.GetRootGameObjects();
                    int before = changed;
                    foreach (var (item, target) in asset)
                        if (ApplyTo(FindInScene(roots, target.indexPath), target.name, item, created, recordUndo: wasOpen)) changed++;
                    if (wasOpen && changed > before) EditorSceneManager.MarkSceneDirty(scene);
                    return changed > before;
                });
            else
            {
                GameObject root = PrefabUtility.LoadPrefabContents(asset.Key.assetPath);
                try
                {
                    int before = changed;
                    foreach (var (item, target) in asset)
                        if (ApplyTo(ButtonScanner.FindByIndexPath(root.transform, target.indexPath), target.name, item, created, recordUndo: false)) changed++;
                    if (changed > before) PrefabUtility.SaveAsPrefabAsset(root, asset.Key.assetPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        foreach (ProposalItem item in ready) item.applied = stamp;
        Save(run, folder);
        AssetDatabase.SaveAssets();
        if (changed > 0) DesignChangeLog.Note($"applied {changed} approved style proposal(s)");
        return changed;
    }

    public static bool Ready(ProposalItem item) =>
        item.review == "approved" && string.IsNullOrEmpty(item.applied) && item.decision != "skip" && item.decision != ""
        && (!item.proposalImpact.changes || item.acceptLayoutChange || item.decision == "unique");

    /// <summary>
    /// Carries out one approved item on the control at t, which must still have the name it had when audited, so an
    /// edited hierarchy never gets a style meant for another control. Returns whether anything changed.
    /// </summary>
    public static bool ApplyTo(Transform t, string expectedName, ProposalItem item, Dictionary<string, UIComponentStyle> created, bool recordUndo)
    {
        if (t == null || t.name != expectedName) return false;
        if (item.decision == "unique")
        {
            UniqueDesign mark = t.GetComponent<UniqueDesign>() ?? (recordUndo ? Undo.AddComponent<UniqueDesign>(t.gameObject) : t.gameObject.AddComponent<UniqueDesign>());
            mark.reason = item.reason;
            mark.decidedBy = $"{(string.IsNullOrEmpty(item.reviewer) ? Environment.UserName : item.reviewer)}, {DateTime.Now:yyyy-MM-dd}";
        }
        else
        {
            UIComponentStyle style = item.decision == "custom" ? CreateCustom(item, created) : FindStyle(item.decisionStyle);
            if (style == null) return false;
            Restyle(t, style, recordUndo, item.keepOwnSize);
        }
        return true;
    }

    private static UIComponentStyle CreateCustom(ProposalItem item, Dictionary<string, UIComponentStyle> created)
    {
        string key = item.custom.name ?? "";
        if (created.TryGetValue(key, out UIComponentStyle style)) return style;
        style = FindStyle(item.custom.name);
        if (style == null)
        {
            UIComponentStyle baseStyle = FindStyle(item.custom.baseStyle);
            if (baseStyle == null) return null;
            style = BuildCustom(baseStyle, item.custom);
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(baseStyle))?.Replace('\\', '/') ?? DesignSystemAssets.DefaultFolder;
            AssetDatabase.CreateAsset(style, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{style.name}.asset"));
            DesignChangeLog.Note($"created {style.name} for a special control");
        }
        created[key] = style;
        return style;
    }

    // In Play mode the copies would run the game's Awake, and edits would be lost when play stops.
    private static void EnsureEditing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode first: style proposals capture and edit the project in Edit mode only.");
    }

    // ---- Files ----

    public static string ReviewPath(string folder) => Path.Combine(folder, "review.json");

    public static ProposalRun Load(string folder) => JsonUtility.FromJson<ProposalRun>(File.ReadAllText(ReviewPath(folder)));

    public static void Save(ProposalRun run, string folder) => File.WriteAllText(ReviewPath(folder), JsonUtility.ToJson(run, true));

    /// <summary>The newest run folder, or null.</summary>
    public static string Latest() =>
        Directory.Exists(Folder) ? Directory.GetDirectories(Folder).Where(d => File.Exists(ReviewPath(d))).OrderByDescending(d => d).FirstOrDefault()?.Replace('\\', '/') : null;

    private static string Slug(string name) => new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
}

/// <summary>
/// Renders a copy of a control's screen through a private camera. UI doesn't draw in preview scenes here, so the
/// copy lives in the open scene, far from everything, hidden and never saved, and is destroyed after each shot.
/// </summary>
internal sealed class CaptureRig : IDisposable
{
    private const int Width = 1920, Height = 1080;
    private static readonly Color Marker = new Color(1f, 0.18f, 0.58f);

    public class Shot
    {
        public byte[] Png;
        public RectInt Crop;
        /// <summary>The control first, then its siblings and its parent.</summary>
        public List<RectRecord> Rects = new List<RectRecord>();
        public RectRecord Target => Rects[0];
        public bool Hidden;
        public string ParentLayout = "none";
        public List<string> SiblingStyles = new List<string>();
    }

    private readonly GameObject rig;
    private readonly Camera cam;
    private readonly RenderTexture rt;
    private readonly Transform canvas;

    public CaptureRig()
    {
        rig = new GameObject("__StyleProposalCapture") { hideFlags = HideFlags.HideAndDontSave };
        rig.transform.position = new Vector3(50000f, 50000f, 0f);
        cam = rig.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
        cam.orthographic = true;
        cam.cullingMask = -1;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 100f;
        rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;

        var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)) { hideFlags = HideFlags.HideAndDontSave };
        canvasGo.transform.SetParent(rig.transform, false);
        var c = canvasGo.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceCamera;
        c.worldCamera = cam;
        c.planeDistance = 10f;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(Width, Height);
        canvas = canvasGo.transform;
    }

    /// <summary>
    /// Copies the screen the control sits on, applies the mutation to the copy's control, and renders. The crop is
    /// the control's layout group (or the control with a margin) unless given, so before and after frame the same area.
    /// </summary>
    public Shot Take(Transform source, Action<Transform> mutate, RectInt? crop,
        IEnumerable<Transform> lookAlikes = null, Action<Transform> mutateLookAlikes = null)
    {
        Transform top = Top(source);
        string path = ButtonScanner.IndexPath(top, source);
        var holder = new GameObject("Copy", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
        holder.SetActive(false);
        holder.transform.SetParent(canvas, false);
        Stretch((RectTransform)holder.transform);
        GameObject copy = Object.Instantiate(top.gameObject, holder.transform, false);
        try
        {
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            Silence(copy);
            holder.SetActive(true);
            if (copy.TryGetComponent(out Canvas _) && copy.transform is RectTransform fill) Stretch(fill);
            Transform target = ButtonScanner.FindByIndexPath(copy.transform, path);
            if (!(target is RectTransform targetRect)) return null;
            var shot = new Shot { Hidden = Reveal(target, copy.transform) };
            Isolate(Context(target), copy.transform);
            Rebuild(target, copy.transform);
            mutate?.Invoke(target);
            if (mutateLookAlikes != null)
                foreach (Transform other in lookAlikes ?? Enumerable.Empty<Transform>())
                {
                    if (!other.IsChildOf(top)) continue;
                    Transform copied = ButtonScanner.FindByIndexPath(copy.transform, ButtonScanner.IndexPath(top, other));
                    if (copied != null && copied != target) mutateLookAlikes(copied);
                }
            Rebuild(target, copy.transform);

            shot.Rects = Records(targetRect);
            Describe(targetRect, shot);
            RectInt area = crop ?? Frame(targetRect);
            shot.Crop = area;
            shot.Png = Render(area, ScreenRect(targetRect));
            return shot;
        }
        finally
        {
            Object.DestroyImmediate(holder);
        }
    }

    private static void Stretch(RectTransform t)
    {
        t.anchorMin = Vector2.zero;
        t.anchorMax = Vector2.one;
        t.offsetMin = t.offsetMax = Vector2.zero;
        t.localScale = Vector3.one;
    }

    private static readonly HashSet<string> DrawingAssemblies = new HashSet<string> { "UnityEngine.UI", "Unity.TextMeshPro", "DesignSystem" };

    // Game scripts that run in edit mode would wake up on the copy — a singleton finding a "duplicate", a renderer
    // rebuilding its charts. The copy is still inactive here, so removing them means none of them ever runs.
    private static void Silence(GameObject copy)
    {
        foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true).Reverse())
        {
            if (behaviour == null) continue;
            Type type = behaviour.GetType();
            if (DrawingAssemblies.Contains(type.Assembly.GetName().Name)) continue;
            if (type.IsDefined(typeof(ExecuteAlways), true) || type.IsDefined(typeof(ExecuteInEditMode), true))
                Object.DestroyImmediate(behaviour);
        }
    }

    // The outermost RectTransform above the control: a whole screen when it has one, so the layout is the real one.
    private static Transform Top(Transform t)
    {
        Transform top = t;
        for (Transform p = t.parent; p != null && p is RectTransform; p = p.parent) top = p;
        return top;
    }

    // Controls on hidden panels (an inactive popup, a faded-out modal) are shown in the copy so they can be seen.
    private static bool Reveal(Transform target, Transform root)
    {
        bool hidden = false;
        for (Transform t = target; t != null; t = t == root ? null : t.parent)
        {
            if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); hidden = true; }
            if (t.TryGetComponent(out CanvasGroup group) && group.alpha < 0.05f) { group.alpha = 1f; hidden = true; }
        }
        return hidden;
    }

    // What a reviewer needs to see: the control's row or column, else the control.
    private static Transform Context(Transform target) =>
        target.parent != null && target.parent.GetComponent<LayoutGroup>() != null ? target.parent : target;

    // Other panels of the same screen (a hidden popup now revealed, a sibling tab) would draw over the control.
    // Everything that isn't the context, inside it, or behind it is hidden; the backgrounds it sits on stay.
    private static void Isolate(Transform context, Transform root)
    {
        for (Transform keep = context; keep != root && keep.parent != null; keep = keep.parent)
            foreach (Transform sibling in keep.parent)
                if (sibling != keep) sibling.gameObject.SetActive(false);
    }

    // In the game, nested size-to-content layouts settle over a few frames. Here they get the same chance: each
    // ancestor from the control up is rebuilt, more than once, so a card that grew pushes its column and panel too.
    private static void Rebuild(Transform target, Transform root)
    {
        // A label whose font or size just changed reports its old height until its mesh is rebuilt.
        foreach (TMPro.TMP_Text text in root.GetComponentsInChildren<TMPro.TMP_Text>()) text.ForceMeshUpdate();
        Canvas.ForceUpdateCanvases();
        for (int pass = 0; pass < 3; pass++)
            for (Transform t = target; t != null; t = t == root ? null : t.parent)
                if (t is RectTransform rt) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        Canvas.ForceUpdateCanvases();
    }

    private List<RectRecord> Records(RectTransform target)
    {
        var records = new List<RectRecord> { Record(target, "control") };
        if (target.parent is RectTransform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i) != target && parent.GetChild(i).gameObject.activeInHierarchy && parent.GetChild(i) is RectTransform sibling)
                    records.Add(Record(sibling, $"{i}:{sibling.name}"));
            records.Add(Record(parent, "parent"));
        }
        return records;
    }

    private RectRecord Record(RectTransform t, string name)
    {
        Rect r = ScreenRect(t);
        return new RectRecord { name = name, x = r.x, y = r.y, w = r.width, h = r.height };
    }

    private static void Describe(RectTransform target, Shot shot)
    {
        if (!(target.parent is RectTransform parent)) return;
        if (parent.TryGetComponent(out HorizontalOrVerticalLayoutGroup group))
            shot.ParentLayout = $"{(group is HorizontalLayoutGroup ? "row" : "column")}, controls child width {(group.childControlWidth ? "on" : "off")}, height {(group.childControlHeight ? "on" : "off")}";
        else if (parent.TryGetComponent(out GridLayoutGroup grid))
            shot.ParentLayout = $"grid, cells {grid.cellSize.x:0}×{grid.cellSize.y:0}";
        shot.SiblingStyles = parent.GetComponentsInChildren<StyledElement>(true)
            .Where(s => s.transform != target && s.ComponentStyle != null).GroupBy(s => s.ComponentStyle.name)
            .OrderByDescending(g => g.Count()).Select(g => $"{g.Key} ×{g.Count()}").ToList();
    }

    private Rect ScreenRect(RectTransform t)
    {
        var corners = new Vector3[4];
        t.GetWorldCorners(corners);
        Vector3 a = cam.WorldToScreenPoint(corners[0]), b = cam.WorldToScreenPoint(corners[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // The control's row or column when that's a sensible size to look at; otherwise the control with a margin.
    private RectInt Frame(RectTransform target)
    {
        Rect area = ScreenRect(target);
        if (target.parent is RectTransform parent && parent.GetComponent<LayoutGroup>() != null)
        {
            Rect group = ScreenRect(parent);
            if (group.width <= 1400f && group.height <= 800f) area = group;
        }
        float pad = Mathf.Max(48f, Mathf.Max(area.width, area.height) * 0.35f);
        int xMin = Mathf.Clamp(Mathf.FloorToInt(area.xMin - pad), 0, Width - 1);
        int yMin = Mathf.Clamp(Mathf.FloorToInt(area.yMin - pad), 0, Height - 1);
        int xMax = Mathf.Clamp(Mathf.CeilToInt(area.xMax + pad), xMin + 1, Width);
        int yMax = Mathf.Clamp(Mathf.CeilToInt(area.yMax + pad), yMin + 1, Height);
        return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    private byte[] Render(RectInt area, Rect target)
    {
        cam.Render();
        RenderTexture previous = RenderTexture.active;
        var tex = new Texture2D(area.width, area.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(area.x, area.y, area.width, area.height), 0, 0);
            Outline(tex, new RectInt(Mathf.RoundToInt(target.x) - area.x - 3, Mathf.RoundToInt(target.y) - area.y - 3,
                                     Mathf.RoundToInt(target.width) + 6, Mathf.RoundToInt(target.height) + 6));
            tex.Apply();
            return tex.EncodeToPNG();
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(tex);
        }
    }

    // A thin marker around the control so a reviewer sees which one in the row is meant.
    private static void Outline(Texture2D tex, RectInt r)
    {
        for (int i = 0; i < 2; i++)
        {
            for (int x = r.xMin; x <= r.xMax; x++) { Plot(tex, x, r.yMin + i); Plot(tex, x, r.yMax - i); }
            for (int y = r.yMin; y <= r.yMax; y++) { Plot(tex, r.xMin + i, y); Plot(tex, r.xMax - i, y); }
        }
    }

    private static void Plot(Texture2D tex, int x, int y)
    {
        if (x >= 0 && y >= 0 && x < tex.width && y < tex.height) tex.SetPixel(x, y, Marker);
    }

    public void Dispose()
    {
        cam.targetTexture = null;
        Object.DestroyImmediate(rig);
        rt.Release();
        Object.DestroyImmediate(rt);
    }
}
