using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Where a checked hierarchy lives, so issues can name it and fixes can edit it in place.</summary>
public struct CheckScope
{
    public string AssetPath;
    /// <summary>The prefab path or scene name issues are listed under.</summary>
    public string Label;
    /// <summary>A loaded prefab asset: fixes edit it through its prefab contents and save it.</summary>
    public bool PrefabAsset;
    /// <summary>The objects can be selected and fixed — prefab assets and scenes that were open during the check.</summary>
    public bool Live;

    public string Where(Transform t) => $"{Label} › {ButtonScanner.HierarchyPath(t)}";

    /// <summary>Whether this hierarchy owns the component, rather than a nested prefab or prefab instance inside it.</summary>
    public bool Owns(Component c) =>
        PrefabAsset ? PrefabUtility.GetCorrespondingObjectFromSource(c) == null : !PrefabUtility.IsPartOfPrefabInstance(c);
}

/// <summary>
/// Edits components where they live: in an open scene through Undo, in a prefab asset through its loaded contents,
/// which are saved afterwards. Prefab assets can't take added or removed components directly.
/// </summary>
public static class ControlEdits
{
    public static void Run(CheckScope scope, IList<Component> targets, Action<List<Component>, bool> edit)
    {
        if (!scope.PrefabAsset)
        {
            edit(targets.Where(t => t != null).ToList(), true);
            return;
        }
        var located = targets.Where(t => t != null).Select(t => (path: ButtonScanner.IndexPath(t.transform.root, t.transform), type: t.GetType())).ToList();
        GameObject root = PrefabUtility.LoadPrefabContents(scope.AssetPath);
        try
        {
            List<Component> loaded = located.Select(l => ButtonScanner.FindByIndexPath(root.transform, l.path)?.GetComponent(l.type))
                                             .Where(c => c != null).ToList();
            edit(loaded, false);
            PrefabUtility.SaveAsPrefabAsset(root, scope.AssetPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static void Remove(Component c, bool undo)
    {
        if (undo) Undo.DestroyObjectImmediate(c);
        else Object.DestroyImmediate(c);
    }
}

/// <summary>
/// What can go wrong with toggles that a single control's check can't see: a group whose toggles disagree, a script
/// that recolours what the style's On state also colours, an On state nobody can see, and a toggle still carrying a
/// button style.
/// </summary>
public static class ToggleChecks
{
    public const string GroupMixed = "Toggle group doesn't match";
    public const string ScriptConflict = "A script may recolour it";
    public const string OnHidden = "On state hard to see";
    public const string ButtonOnToggle = "Toggle uses a button style";

    public static void CheckGroups(GameObject root, CheckScope scope, List<DesignIssue> issues)
    {
        Toggle[] toggles = root.GetComponentsInChildren<Toggle>(true);
        foreach (ToggleGroup group in root.GetComponentsInChildren<ToggleGroup>(true))
        {
            if (!scope.Owns(group)) continue;
            List<Toggle> members = toggles.Where(t => t.group == group).ToList();
            if (members.Count < 2) continue;
            List<UIComponentStyle> styles = members.Select(t => t.TryGetComponent(out StyledElement s) ? s.ComponentStyle : null).ToList();
            if (styles.Distinct().Count() < 2) continue;

            UIComponentStyle common = styles.OfType<UIToggleStyle>().GroupBy(s => s).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            string summary = string.Join(", ", styles.GroupBy(s => s != null ? s.name : "no style").Select(g => $"{g.Count()} × {g.Key}"));
            bool canFix = scope.Live && common != null && members.All(scope.Owns);
            issues.Add(new DesignIssue
            {
                Severity = DesignIssue.Level.Warning, Category = GroupMixed, Where = scope.Where(group.transform),
                Target = scope.Live ? group : null, AssetPath = scope.AssetPath,
                Message = $"Its toggles don't share one style ({summary}), so they can look different from each other.",
                FixLabel = canFix ? $"Give every toggle in the group {common.name}" : null,
                Fix = canFix ? () => ControlEdits.Run(scope, members.Cast<Component>().ToList(), (loaded, undo) =>
                {
                    foreach (Toggle t in loaded.OfType<Toggle>()) Restyle(t, common, undo);
                }) : (Action)null,
            });
        }
    }

    // A toggle carrying another kind's style loses it first; the look it shows stays until the new style applies.
    private static void Restyle(Toggle toggle, UIComponentStyle style, bool undo)
    {
        if (toggle.TryGetComponent(out StyledElement current) && !(current is StyledToggle)) ControlEdits.Remove(current, undo);
        if (toggle.TryGetComponent(out StyledToggle styled) && styled.Style == style) return;
        DesignSystemAssets.Attach(toggle, style, undo);
    }

    /// <summary>
    /// A script holding a toggle's background can recolour it in code, and then it fights the style's On colour —
    /// the quiz option pattern. Read from serialized references, so any script is caught without naming it.
    /// </summary>
    public static void CheckScripts(GameObject root, CheckScope scope, List<DesignIssue> issues)
    {
        Dictionary<Object, StyledToggle> backgrounds = root.GetComponentsInChildren<StyledToggle>(true)
            .Where(t => scope.Owns(t) && t.Style != null && t.Style.ChangesWhenOn && t.Background != null)
            .GroupBy(t => (Object)t.Background).ToDictionary(g => g.Key, g => g.First());
        if (backgrounds.Count == 0) return;

        var reported = new HashSet<(MonoBehaviour, StyledToggle)>();
        foreach (MonoBehaviour script in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (script == null || IsFramework(script)) continue;
            SerializedProperty it = new SerializedObject(script).GetIterator();
            while (it.NextVisible(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                if (!backgrounds.TryGetValue(it.objectReferenceValue, out StyledToggle toggle) || !reported.Add((script, toggle))) continue;
                issues.Add(new DesignIssue
                {
                    Severity = DesignIssue.Level.Warning, Category = ScriptConflict, Where = scope.Where(toggle.transform),
                    Target = scope.Live ? toggle : null, AssetPath = scope.AssetPath,
                    Message = $"{script.GetType().Name} also holds its background ({it.displayName}). If it recolours it in code, the two " +
                              $"fight over the selected look. Keep one: clear {toggle.Style.name}'s When on colours, or stop the script colouring it.",
                });
            }
        }
    }

    // Unity's own UI and the design system's components reference backgrounds without fighting over their colour.
    private static bool IsFramework(MonoBehaviour script) =>
        script is UIBehaviour || script is StyledElement || script is StyledText || script is StyledColor
        || (script.GetType().Namespace ?? "").StartsWith("UnityEngine");

    /// <summary>Card and tab styles show On only through the style; a checkbox or radio has its checkmark.</summary>
    public static void CheckStyle(UIComponentStyle style, List<DesignIssue> issues)
    {
        if (!(style is UIToggleStyle toggle) || toggle.ToggleType == ToggleType.Checkbox || toggle.ToggleType == ToggleType.Radio) return;
        string path = AssetDatabase.GetAssetPath(style);
        if (!toggle.ChangesWhenOn)
        {
            issues.Add(new DesignIssue
            {
                Severity = DesignIssue.Level.Note, Category = OnHidden, Where = style.name, Target = style, AssetPath = path,
                Message = "Nothing in the style shows which option is on. Fine if a script shows it; otherwise set When on ▸ Fill.",
            });
            return;
        }
        float fill = ButtonScanner.ColorDistance(StyleConsolidation.VisibleColour(toggle, false), StyleConsolidation.VisibleColour(toggle, true));
        bool labelDiffers = toggle.LabelColorFor(false).TryResolve(toggle.Theme, out Color off) && toggle.LabelColorFor(true).TryResolve(toggle.Theme, out Color on)
                            && ButtonScanner.ColorDistance(off, on) >= 0.15f;
        if (fill < 0.15f && !labelDiffers && !toggle.OnShape.IsSet)
            issues.Add(new DesignIssue
            {
                Severity = DesignIssue.Level.Warning, Category = OnHidden, Where = style.name, Target = style, AssetPath = path,
                Message = "On and Off look almost the same, so players can barely tell which option is selected. Make When on ▸ Fill stand out more.",
            });
    }

    /// <summary>A toggle styled before toggle styles existed can't have its On state styled until it moves over.</summary>
    public static void CheckButtonStyle(StyledElement styled, CheckScope scope, List<DesignIssue> issues)
    {
        if (!(styled is StyledButton) || !styled.TryGetComponent(out Toggle _)) return;
        issues.Add(new DesignIssue
        {
            Severity = DesignIssue.Level.Warning, Category = ButtonOnToggle, Where = scope.Where(styled.transform),
            Target = scope.Live ? styled : null, AssetPath = scope.AssetPath,
            Message = $"It's a toggle styled with the button style {styled.ComponentStyle.name}, so its On state can't be styled. " +
                      "Removing the button style keeps its look; then right-click ▸ Design System ▸ Style Children gives it a toggle style.",
            FixLabel = scope.Live ? "Remove the button style (keeps its look)" : null,
            Fix = scope.Live ? () => ControlEdits.Run(scope, new List<Component> { styled }, (loaded, undo) =>
            {
                foreach (Component c in loaded) ControlEdits.Remove(c, undo);
            }) : (Action)null,
            FixKeepsLook = scope.Live,
        });
    }
}
