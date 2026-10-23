using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A <see cref="StyledElement"/> on a clickable control: it also drives the control's hover, press and selection
/// tints.
///
/// Works on both control shapes in this project: an Image on the control itself, and a transparent hit area whose
/// visible background is a child — <see cref="FindBackground"/> picks whichever actually draws, and the control's
/// target graphic is pointed at it so state tints land on something visible.
/// </summary>
[RequireComponent(typeof(Selectable))]
public abstract class StyledSelectable : StyledElement
{
    private Selectable selectable;

    protected Selectable Selectable => selectable != null ? selectable : selectable = GetComponent<Selectable>();

    protected override Image DetectBackground() => FindBackground(Selectable);
    protected override TMP_Text DetectLabel() => FindLabel(Selectable);
    protected override Graphic DetectIcon() => Selectable is Toggle toggle ? toggle.graphic : FindIcon(Selectable, Background);

    protected override bool ApplyStates(UIComponentStyle style)
    {
        Selectable s = Selectable;
        if (!style.ApplyStateTints || s == null) return false;
        bool changed = false;
        if (s.transition != Selectable.Transition.ColorTint) { s.transition = Selectable.Transition.ColorTint; changed = true; }
        if (Background != null && s.targetGraphic != Background) { s.targetGraphic = Background; changed = true; }
        ColorBlock tints = style.StateColors;
        if (s.colors != tints) { s.colors = tints; changed = true; }
        return changed;
    }

    /// <summary>
    /// The icon image: the first visible child Image that isn't the background and isn't a progress or highlight
    /// ring. Only coloured when the style sets an icon colour, so a decorative image picked up here is harmless.
    /// </summary>
    public static Image FindIcon(Selectable s, Image background, Graphic exclude = null)
    {
        if (s == null) return null;
        foreach (Image img in s.GetComponentsInChildren<Image>(true))
            if (img != background && img != exclude && img.gameObject != s.gameObject && img.sprite != null && img.type != Image.Type.Filled
                && img.enabled && img.color.a > 0.01f && IsActiveUnder(img.transform, s.transform))
                return img;
        return null;
    }

    /// <summary>The text that names the control: an input field's text, a dropdown's caption, else the first TMP text.</summary>
    public static TMP_Text FindLabel(Selectable s) => s switch
    {
        TMP_InputField input when input.textComponent != null => input.textComponent,
        TMP_Dropdown dropdown when dropdown.captionText != null => dropdown.captionText,
        _ => s != null ? s.GetComponentInChildren<TMP_Text>(true) : null,
    };

    /// <summary>
    /// The Image that visibly draws this control: its target graphic or own Image when visible, otherwise the
    /// largest visible, active, non-Filled child Image. Filled images are progress rings and highlight circles,
    /// never the control's shape.
    /// </summary>
    public static Image FindBackground(Selectable b)
    {
        if (b == null) return null;
        if (b.targetGraphic is Image target && IsVisible(target)) return target;
        b.TryGetComponent(out Image own);
        if (own != null && IsVisible(own)) return own;

        Image best = null;
        float bestArea = 0f;
        foreach (Image img in b.GetComponentsInChildren<Image>(true))
        {
            if (img == own || !IsVisible(img) || img.type == Image.Type.Filled || !IsActiveUnder(img.transform, b.transform))
                continue;
            Rect r = img.rectTransform.rect;
            float area = r.width * r.height;
            if (area > bestArea) { best = img; bestArea = area; }
        }
        return best != null ? best : own;
    }
}
