using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A layout setup that will fight or collapse, with an optional one-click fix.</summary>
public class LayoutIssue
{
    public string Message;
    public string FixLabel;
    /// <summary>Only offered when applying it leaves the button looking the same.</summary>
    public Action Fix;
    /// <summary>Worth knowing but not broken: the button looks as intended today.</summary>
    public bool Benign;
    /// <summary>The fix leaves every pixel as it is, so it can be applied in bulk without a look first.</summary>
    public bool FixKeepsLook;
}

/// <summary>
/// Spots the Content Size Fitter setups that break layouts. Never changes anything by itself: the tools report
/// these, and a person applies the fix — every fix is undoable and only ever relaxes one fitter axis.
/// </summary>
public static class LayoutDiagnostics
{
    public static List<LayoutIssue> Diagnose(RectTransform rt, UIComponentStyle style = null)
    {
        var issues = new List<LayoutIssue>();
        if (rt == null || !rt.TryGetComponent(out ContentSizeFitter fitter)) return issues;

        foreach (bool vertical in new[] { false, true })
        {
            ContentSizeFitter.FitMode mode = vertical ? fitter.verticalFit : fitter.horizontalFit;
            if (mode == ContentSizeFitter.FitMode.Unconstrained) continue;
            string axis = vertical ? "height" : "width";

            bool v = vertical;
            if (ParentControls(rt, vertical))
            {
                issues.Add(ParentExpands(rt, vertical)
                    ? new LayoutIssue
                    {
                        Benign = true,
                        Message = $"Its parent stretches it to fill the {axis}, and its Content Size Fitter shrinks it back to fit its " +
                                  "content. The fitter runs last, so it looks as intended — Unity only warns about the setup. Making it fill " +
                                  $"the {axis} instead means setting the fitter's {axis} to Unconstrained, which changes the look.",
                    }
                    : new LayoutIssue
                    {
                        Message = $"Its Content Size Fitter sets the {axis}, but the parent layout group already sizes it to fit its " +
                                  "content. The fitter is redundant and can make the size jitter.",
                        FixLabel = $"Remove the redundant fitter {axis} (no visible change)",
                        Fix = () => Unconstrain(fitter, v),
                        FixKeepsLook = true,
                    });
                continue;
            }

            string problem =
                StyledSelectable.IsStretched(rt, vertical)
                    ? $"It stretches with its parent on the {axis}, and its Content Size Fitter resizes it as well. The two fight."
                : !HasSomethingToMeasure(rt, vertical, style)
                    ? $"Its Content Size Fitter has nothing to measure for the {axis} — no layout group or Layout Element — so the " +
                      "button shrinks to its sprite's size."
                : null;
            if (problem == null) continue;

            issues.Add(new LayoutIssue
            {
                Message = problem,
                FixLabel = $"Set the fitter's {axis} to Unconstrained",
                Fix = () => Unconstrain(fitter, v),
            });
        }
        return issues;
    }

    // Force-expand makes the parent hand the child all the space on that axis (a grid always hands it the cell),
    // so the parent's size and the fitter's differ — the fitter, running later, is what keeps the button compact.
    private static bool ParentExpands(RectTransform rt, bool vertical)
    {
        if (rt.parent.TryGetComponent(out GridLayoutGroup _)) return true;
        return rt.parent.TryGetComponent(out HorizontalOrVerticalLayoutGroup group)
               && (vertical ? group.childForceExpandHeight : group.childForceExpandWidth);
    }

    private static bool ParentControls(RectTransform rt, bool vertical)
    {
        if (rt.parent == null) return false;
        if (rt.parent.TryGetComponent(out GridLayoutGroup _)) return true;
        return rt.parent.TryGetComponent(out HorizontalOrVerticalLayoutGroup group)
               && (vertical ? group.childControlHeight : group.childControlWidth);
    }

    // A style size counts: applying it writes a Layout Element, which gives the fitter its number.
    private static bool HasSomethingToMeasure(RectTransform rt, bool vertical, UIComponentStyle style)
    {
        if (rt.TryGetComponent(out LayoutGroup _)) return true;
        if (rt.TryGetComponent(out LayoutElement e) && (vertical ? e.preferredHeight >= 0f || e.minHeight >= 0f
                                                                 : e.preferredWidth >= 0f || e.minWidth >= 0f))
            return true;
        return style != null && (vertical ? style.Height > 0f : style.Width > 0f || style.MinWidth > 0f);
    }

    private static void Unconstrain(ContentSizeFitter fitter, bool vertical)
    {
        if (fitter == null) return;
        Undo.RecordObject(fitter, "Relax Content Size Fitter");
        if (vertical) fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        else fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        EditorUtility.SetDirty(fitter);
        if (PrefabUtility.IsPartOfPrefabInstance(fitter)) PrefabUtility.RecordPrefabInstancePropertyModifications(fitter);
    }
}
