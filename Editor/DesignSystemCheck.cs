using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One thing a player would see as broken, or a designer should know before shipping.</summary>
public class DesignIssue
{
    public enum Level { Problem, Warning, Note }

    public Level Severity;
    public string Category;
    public string Message;
    public string Where;
    /// <summary>The object to select — null once its scene has been closed again.</summary>
    public UnityEngine.Object Target;
    /// <summary>The file to ping when the object itself can't be selected.</summary>
    public string AssetPath;
    public string FixLabel;
    public Action Fix;
    /// <summary>The fix never changes a visible pixel, so "Fix safe issues" may apply it without a look first.</summary>
    public bool FixKeepsLook;
}

/// <summary>
/// The safety net for style changes: scans every styled control in prefabs and in-scope scenes and reports what
/// would look broken — labels that no longer fit, layout conflicts, pictures styled as shapes, colours that were
/// deleted, heights a control can't take — plus the toggle problems in <see cref="ToggleChecks"/>. Reads only;
/// every fix is a separate, explicit click.
/// </summary>
public static class DesignSystemCheck
{
    public const string LabelFitCategory = "Label doesn't fit";
    public const string Layout = "Layout conflict";
    public const string LayoutNote = "Layout works, Unity warns";
    public const string Picture = "Picture styled as a shape";
    public const string MissingToken = "Missing colour";
    public const string Stretched = "Ignores the style height";
    public const string ThirdPartySprite = "Sprite in a third-party folder";
    public const string CardDiffers = "Drag card differs from the standard";
    public const string CardNoHandle = "Drag card has no handle";

    public static List<DesignIssue> Run(DesignSystemSettings settings, bool includeScenes = true)
    {
        var issues = new List<DesignIssue>();
        CheckStyles(issues);
        CheckDragCardStyles(DesignSystemAssets.FindAll<UIDragCardStyle>(), DragCardStandard.Find(), issues);
        CheckSpriteHomes(settings, issues);
        TextFitCheck.Run(settings, issues);

        try
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (settings.IsExcluded(path) || path.StartsWith(DesignSystemAssets.DefaultFolder + "/")) continue;
                if (i % 25 == 0 && EditorUtility.DisplayCancelableProgressBar("Checking prefabs", path, (float)i / guids.Length)) return issues;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                CheckHierarchy(asset, new CheckScope { AssetPath = path, Label = path, PrefabAsset = true, Live = true }, issues);
            }

            List<string> scenes = includeScenes ? settings.ScenePaths() : new List<string>();
            for (int i = 0; i < scenes.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Checking scenes", scenes[i], (float)i / Math.Max(1, scenes.Count));
                ButtonScanner.WithScene(scenes[i], (scene, wasOpen) =>
                {
                    var scope = new CheckScope { AssetPath = scene.path, Label = scene.name, Live = wasOpen };
                    foreach (GameObject root in scene.GetRootGameObjects()) CheckHierarchy(root, scope, issues);
                    return false;
                });
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        return issues.OrderBy(i => i.Severity).ThenBy(i => i.Category).ToList();
    }

    private static void CheckSpriteHomes(DesignSystemSettings settings, List<DesignIssue> issues)
    {
        foreach (string path in ThirdPartySprites.Find(DesignSystemAssets.FindAll<UITheme>(), DesignSystemAssets.FindAll<UIComponentStyle>(), settings))
        {
            string p = path;
            issues.Add(new DesignIssue
            {
                Severity = DesignIssue.Level.Warning, Category = ThirdPartySprite, Where = path,
                Target = AssetDatabase.LoadMainAssetAtPath(path), AssetPath = path,
                Message = "Styles draw with this sprite, but it lives in a third-party folder: removing or updating that pack would break them.",
                FixLabel = $"Move it to {ThirdPartySprites.Folder} (every reference follows)",
                Fix = () => ThirdPartySprites.Move(p), FixKeepsLook = true,
            });
        }
    }

    /// <summary>
    /// Notes how each drag card family differs from the standard card, with the change following it would make —
    /// a report to decide from, never applied unasked. A family that already looks like the standard can follow it
    /// as a safe fix.
    /// </summary>
    public static void CheckDragCardStyles(IEnumerable<UIDragCardStyle> styles, UIDragCardStyle standard, List<DesignIssue> issues)
    {
        List<UIDragCardStyle> list = styles.Where(s => s != null).ToList();
        if (list.Count == 0) return;
        if (standard == null)
        {
            issues.Add(new DesignIssue
            {
                Severity = DesignIssue.Level.Note, Category = CardDiffers, Where = "Drag cards",
                Message = "No standard drag card yet. On the Styles tab, open a drag card style's ⋯ menu ▸ Set as the standard drag card.",
            });
            return;
        }
        foreach (UIDragCardStyle style in list.Where(s => !DragCardStandard.Follows(s, standard)))
        {
            List<string> changes = DragCardStandard.Differences(style, standard);
            UIDragCardStyle s = style;
            issues.Add(new DesignIssue
            {
                Severity = DesignIssue.Level.Note, Category = CardDiffers, Where = style.name, Target = style,
                AssetPath = AssetDatabase.GetAssetPath(style),
                Message = changes.Count == 0
                    ? $"Looks like {standard.name} but doesn't follow it, so a change to the standard won't reach these cards."
                    : $"Differs from {standard.name}: {string.Join("; ", changes)}.",
                FixLabel = changes.Count == 0 ? "Follow the standard (nothing changes on screen)" : "Follow the standard handle (changes the cards above)",
                Fix = () => DragCardStandard.Follow(s, standard),
                FixKeepsLook = changes.Count == 0,
            });
        }
    }

    /// <summary>Every per-control and per-hierarchy check on one prefab or scene root.</summary>
    public static void CheckHierarchy(GameObject root, CheckScope scope, List<DesignIssue> issues)
    {
        UIDragCardStyle standard = DragCardStandard.Find();
        foreach (StyledDragCard card in root.GetComponentsInChildren<StyledDragCard>(true))
            if (scope.Owns(card) && card.Handle == null) NoteMissingHandle(card, standard, scope, issues);
        foreach (StyledElement b in root.GetComponentsInChildren<StyledElement>(true))
            if (scope.Owns(b)) CheckButton(b, scope, issues);
        ToggleChecks.CheckGroups(root, scope, issues);
        ToggleChecks.CheckScripts(root, scope, issues);
    }

    private static void NoteMissingHandle(StyledDragCard card, UIDragCardStyle standard, CheckScope scope, List<DesignIssue> issues)
    {
        bool canFix = scope.Live && DragCardStandard.CanBuildHandle(standard) && DragCardStandard.CanAddHandle(card);
        issues.Add(new DesignIssue
        {
            Severity = DesignIssue.Level.Note, Category = CardNoHandle, Where = scope.Where(card.transform),
            Target = scope.Live ? card : null, AssetPath = scope.AssetPath,
            Message = canFix
                ? "This card has no handle. Adding the standard one moves its content right by the handle's width."
                : !DragCardStandard.CanBuildHandle(standard) ? "This card has no handle. Set a standard drag card with a handle colour and grip icon to add one."
                : "This card has no handle, and lays out its children without a row layout, so one has to be added by hand.",
            FixLabel = canFix ? "Add the standard handle (changes the layout)" : null,
            Fix = canFix ? () => ControlEdits.Run(scope, new List<Component> { card }, (loaded, undo) =>
            {
                foreach (StyledDragCard c in loaded.OfType<StyledDragCard>()) DragCardStandard.AddHandle(c, standard, undo);
            }) : (System.Action)null,
        });
    }

    private static void CheckStyles(List<DesignIssue> issues)
    {
        foreach (UIComponentStyle style in DesignSystemAssets.FindAll<UIComponentStyle>())
        {
            ToggleChecks.CheckStyle(style, issues);
            string path = AssetDatabase.GetAssetPath(style);
            if (DesignSystemRepair.NeedsRepair(style))
            {
                UIComponentStyle s = style;
                issues.Add(new DesignIssue
                {
                    Severity = DesignIssue.Level.Problem, Category = Picture, Where = style.name, Target = style, AssetPath = path,
                    Message = "Its sprite is a picture (icon, character, checkbox), but the style recolours it flat or stretches it to button height.",
                    FixLabel = "Restore original look", Fix = () => DesignSystemRepair.Repair(new[] { s }), FixKeepsLook = true,
                });
            }

            UITheme theme = style.Theme;
            foreach ((string what, ThemeColor colour) in new[] { ("fill", style.Background.fill), ("label colour", style.LabelColor), ("icon colour", style.IconColor) })
                if (colour.IsSet && (theme == null || theme.GetToken(colour.tokenId) == null))
                    issues.Add(new DesignIssue
                    {
                        Severity = DesignIssue.Level.Problem, Category = MissingToken, Where = style.name, Target = style, AssetPath = path,
                        Message = $"Its {what} points at a colour that no longer exists in the theme, so that colour isn't applied. Pick one in the style.",
                    });
        }
    }

    private static void CheckButton(StyledElement b, CheckScope scope, List<DesignIssue> issues)
    {
        UIComponentStyle style = b.ComponentStyle;
        if (style == null) return;
        var rt = (RectTransform)b.transform;
        string where = scope.Where(b.transform), assetPath = scope.AssetPath;
        bool live = scope.Live;
        ToggleChecks.CheckButtonStyle(b, scope, issues);

        if (style.Height > 0f && StyledSelectable.IsStretched(rt, vertical: true))
            issues.Add(new DesignIssue
            {
                Severity = DesignIssue.Level.Note, Category = Stretched, Where = where, Target = live ? b : null, AssetPath = assetPath,
                Message = $"It stretches vertically with its parent, so it keeps the parent's height instead of {style.Height:0.#} px.",
            });

        bool sized = b is StyledSelectable || style.ControlsSize;
        if (sized)
        foreach (LayoutIssue layout in LayoutDiagnostics.Diagnose(rt, style))
            issues.Add(new DesignIssue
            {
                Severity = layout.Benign ? DesignIssue.Level.Note : DesignIssue.Level.Warning,
                Category = layout.Benign ? LayoutNote : Layout, Where = where, Target = live ? b : null, AssetPath = assetPath,
                Message = layout.Message, FixLabel = live ? layout.FixLabel : null,
                Fix = live && layout.Fix != null ? () => { layout.Fix(); AssetDatabase.SaveAssets(); } : null,
                FixKeepsLook = live && layout.FixKeepsLook,
            });

        LabelFit fit = sized ? MeasureLabel(b) : default;
        if (fit.Problem != null)
            issues.Add(new DesignIssue
            {
                Severity = DesignIssue.Level.Problem, Category = LabelFitCategory, Where = where, Target = live ? b : null, AssetPath = assetPath,
                Message = fit.Problem,
                FixLabel = live && fit.WidenTo > 0f ? $"Widen the button to {fit.WidenTo:0} px so the label fits on one line" : null,
                Fix = live && fit.WidenTo > 0f ? () => Widen(b, fit.WidenTo) : null,
            });
    }

    /// <summary>
    /// Why the label won't fit its button, or null when it does. Measured as the style will make the button — its
    /// height, width and text size — not as the prefab file last stored it, since a file keeps its old sizes until
    /// it is re-saved. Buttons that grow with their label and auto-sizing text adjust themselves and are skipped.
    /// </summary>
    public static string LabelFitProblem(StyledElement b) => MeasureLabel(b).Problem;

    /// <summary>The fit verdict, plus the button width that would fit the label on one line when widening can help.</summary>
    public struct LabelFit
    {
        public string Problem;
        /// <summary>0 when widening isn't possible or wouldn't help (a stretched or layout-sized button, a fixed style width).</summary>
        public float WidenTo;
    }

    public static LabelFit MeasureLabel(StyledElement b)
    {
        TMP_Text label = b.Label;
        if (label == null || !label.gameObject.activeSelf || string.IsNullOrWhiteSpace(label.text)) return default;

        UIComponentStyle style = b.ComponentStyle;
        var rt = (RectTransform)b.transform;
        bool sized = style != null && !b.KeepSize;

        float buttonW = rt.rect.width, buttonH = rt.rect.height;
        if (sized && !StyledSelectable.IsStretched(rt, vertical: true) && style.Height > 0f) buttonH = style.Height;
        if (sized && !StyledSelectable.IsStretched(rt, vertical: false))
            buttonW = style.Width > 0f ? Mathf.Max(style.Width, style.MinWidth) : Mathf.Max(buttonW, style.MinWidth);
        if (buttonW <= 0f || buttonH <= 0f) return default;

        float fontSize = label.fontSize;
        bool autoSize = label.enableAutoSizing;
        if (style != null && style.TryGetLabelRoleId(out string role) && style.Theme != null && style.Theme.Typography != null)
        {
            TypographyScale.RoleStyle rs = style.Theme.Typography.Get(role);
            if (rs != null) { autoSize = rs.autoSize; fontSize = rs.fontSize; }
        }
        if (style != null && style.LabelSize > 0f) { autoSize = false; fontSize = style.LabelSize; }
        if (autoSize) return default;

        // A label stretched across the button gives up its offsets; one with a fixed rect wraps at its own width but
        // can overflow it freely as long as the text stays inside the button.
        RectTransform lr = label.rectTransform;
        bool labelGrows = label.TryGetComponent(out ContentSizeFitter _);
        bool stretchX = !labelGrows && !Mathf.Approximately(lr.anchorMin.x, lr.anchorMax.x);
        bool stretchY = !labelGrows && !Mathf.Approximately(lr.anchorMin.y, lr.anchorMax.y);
        Vector4 margin = label.margin;
        float roomW = (stretchX ? buttonW + lr.sizeDelta.x : buttonW) - margin.x - margin.z;
        float roomH = (stretchY ? buttonH + lr.sizeDelta.y : buttonH) - margin.y - margin.w;
        float wrapW = (stretchX || labelGrows ? roomW : lr.rect.width - margin.x - margin.z);
        bool wraps = !labelGrows && label.textWrappingMode != TextWrappingModes.NoWrap;
        bool growsWide = StyledSelectable.IsDriven(rt, vertical: false);
        bool growsTall = StyledSelectable.IsDriven(rt, vertical: true);

        // Measured at the size the style gives the text; the asset is only borrowed for the measurement.
        float original = label.fontSize;
        Vector2 needed, oneLine;
        try
        {
            if (!Mathf.Approximately(original, fontSize)) label.fontSize = fontSize;
            oneLine = label.GetPreferredValues(label.text);
            needed = wraps && wrapW > 0f ? label.GetPreferredValues(label.text, wrapW, 0f) : oneLine;
        }
        catch (Exception)
        {
            return default; // A label whose font asset is missing can't be measured; it has bigger problems than fit.
        }
        finally
        {
            if (!Mathf.Approximately(label.fontSize, original)) label.fontSize = original;
        }

        string text = Shorten(label.text);
        bool canWiden = !growsWide && !StyledSelectable.IsStretched(rt, vertical: false) && (style == null || style.Width <= 0f || b.KeepSize);
        float widenTo = canWiden ? Mathf.Ceil(oneLine.x + (buttonW - roomW) + 8f) : 0f;
        if (!wraps && !growsWide && needed.x > roomW + 1f)
            return new LabelFit
            {
                WidenTo = widenTo,
                Problem = $"\"{text}\" needs {needed.x:0} px wide at {fontSize:0.#} px text, but the button is {buttonW:0} px wide. " +
                          "Shorter copy, a smaller text size or a wider button would fit it.",
            };
        if (!growsTall && needed.y > roomH + 1f)
        {
            int lines = oneLine.y > 0f ? Mathf.Max(1, Mathf.RoundToInt(needed.y / oneLine.y)) : 1;
            return lines > 1
                ? new LabelFit
                {
                    WidenTo = widenTo,
                    Problem = $"\"{text}\" wraps onto {lines} lines at {fontSize:0.#} px text and needs {needed.y:0} px, but the button is {buttonH:0} px tall. " +
                              "Shorter copy, a smaller text size or a wider button would keep it on one line.",
                }
                : new LabelFit { Problem = $"\"{text}\" needs {needed.y:0} px tall at {fontSize:0.#} px text, but the button is {buttonH:0} px tall." };
        }
        return default;
    }

    /// <summary>Widens a button to the given width — a visible change, so it is never applied in bulk.</summary>
    public static void Widen(StyledElement b, float width)
    {
        var rt = (RectTransform)b.transform;
        Undo.RecordObject(rt, "Widen button to fit");
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        EditorUtility.SetDirty(rt);
        if (PrefabUtility.IsPartOfPrefabInstance(rt)) PrefabUtility.RecordPrefabInstancePropertyModifications(rt);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Applies every fix that keeps the look. Picture styles are restored in one pass, since each restore re-saves
    /// the prefabs that use the style. Returns the issues fixed.
    /// </summary>
    public static List<DesignIssue> FixSafe(IEnumerable<DesignIssue> issues)
    {
        List<DesignIssue> safe = issues.Where(i => i.FixKeepsLook && i.Fix != null).ToList();
        List<UIComponentStyle> pictures = safe.Where(i => i.Category == Picture).Select(i => i.Target as UIComponentStyle).Where(s => s != null).ToList();
        if (pictures.Count > 0) DesignSystemRepair.Repair(pictures);
        foreach (DesignIssue issue in safe.Where(i => i.Category != Picture)) issue.Fix();
        AssetDatabase.SaveAssets();
        return safe;
    }

    private static string Shorten(string text)
    {
        text = text.Replace("\n", " ");
        return text.Length > 32 ? text.Substring(0, 30) + "…" : text;
    }
}
