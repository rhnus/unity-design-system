using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a draggable card's handle, grip icon and body from a <see cref="UIDragCardStyle"/>. Sits beside the
/// card's own drag script, which keeps every behaviour; this only paints. The card's parts are found by the names
/// the card prefabs share — see <see cref="FindParts"/> — so no prefab needs re-wiring.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class StyledDragCard : StyledElement
{
    [SerializeField] private UIDragCardStyle style;

    [Tooltip("The handle's frame. Auto-detected.")]
    [SerializeField] private Image handle;
    [Tooltip("The handle's inner panel, on a two-tone handle. Auto-detected; empty on a single-colour handle.")]
    [SerializeField] private Image handleInner;
    [Tooltip("The grip icon on the handle. Auto-detected.")]
    [SerializeField] private Image grip;

    public UIDragCardStyle Style { get => style; set { style = value; Apply(); } }
    public override UIComponentStyle ComponentStyle => style;
    public Image Handle => handle;
    public Image HandleInner => handleInner;
    public Image Grip => grip;

    public override void AssignStyle(UIComponentStyle value) =>
        Style = value == null || value is UIDragCardStyle ? (UIDragCardStyle)value
              : throw new System.ArgumentException($"{value.name} is not a drag card style.", nameof(value));

    public override void AutoDetect()
    {
        DragCardParts parts = FindParts(transform);
        if (handle == null) handle = parts.Handle;
        if (handleInner == null) handleInner = parts.HandleInner;
        if (grip == null) grip = parts.Grip;
        base.AutoDetect();
    }

    protected override Image DetectBackground() => FindParts(transform).Body;
    protected override TMP_Text DetectLabel() => FindParts(transform).Title;
    protected override Graphic DetectIcon() => null;

    protected override bool ApplyExtras(UIComponentStyle s)
    {
        UITheme theme = style.Theme;
        bool changed = false;
        if (handle != null && style.SetsHandle) changed |= style.Handle.ApplyTo(handle, theme);
        if (handleInner != null && style.HandleInnerFill.TryResolve(theme, out Color inner) && handleInner.color != inner)
        {
            handleInner.color = inner;
            changed = true;
        }
        if (grip != null && style.GripColor.TryResolve(theme, out Color ink) && grip.color != ink)
        {
            grip.color = ink;
            changed = true;
        }
        if (grip != null && style.GripSprite != null && grip.sprite != style.GripSprite)
        {
            grip.sprite = style.GripSprite;
            changed = true;
        }
        if (handle != null && style.HandleWidth > 0f) changed |= SetHandleWidth(style.HandleWidth);
        return changed;
    }

    // A handle sized by its layout element takes the width there; a free one is resized, unless stretched.
    private bool SetHandleWidth(float width)
    {
        if (handle.TryGetComponent(out LayoutElement element))
        {
            if (Mathf.Approximately(element.preferredWidth, width)) return false;
            element.preferredWidth = width;
            return true;
        }
        RectTransform rt = handle.rectTransform;
        if (IsStretched(rt, vertical: false) || Mathf.Approximately(rt.sizeDelta.x, width)) return false;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        return true;
    }

    public override IEnumerable<Object> WrittenParts()
    {
        if (handle != null) { yield return handle; yield return handle.rectTransform; }
        if (handle != null && handle.TryGetComponent(out LayoutElement element)) yield return element;
        if (handleInner != null) yield return handleInner;
        if (grip != null) yield return grip;
    }

    // ---- Finding a card's parts ----

    /// <summary>The parts of one card, any of which may be missing — an older card has no handle at all.</summary>
    public struct DragCardParts
    {
        public Image Body, Handle, HandleInner, Grip;
        public TMP_Text Title;
    }

    private static readonly string[] HandleNames = { "draggable icon group", "grip", "handle" };
    private static readonly string[] BodyNames = { "body", "bg", "activitybg" };
    private static readonly string[] SkippedNames = { "border", "outline", "validation" };

    /// <summary>
    /// Finds a card's parts by the names the card prefabs share. Handle: a child named "Draggable Icon Group",
    /// "Grip" or "Handle". Grip icon: the first active picture (an unsliced sprite) inside the handle; inner
    /// panel: the first shape inside it. Body: a visible image named "Body", "BG" or "ActivityBG" outside the
    /// handle, else the card's own image. Title: a text named "Label", else one whose parent is named "…Name…",
    /// else the first text outside the handle. Borders, outlines and validation overlays are never the body.
    /// </summary>
    public static DragCardParts FindParts(Transform card)
    {
        var parts = new DragCardParts();
        if (card == null) return parts;

        foreach (Transform t in card.GetComponentsInChildren<Transform>(true))
            if (t != card && Named(t, HandleNames) && t.TryGetComponent(out Image frame))
            {
                parts.Handle = frame;
                break;
            }

        if (parts.Handle != null)
            foreach (Image img in parts.Handle.GetComponentsInChildren<Image>(true))
            {
                if (img == parts.Handle || !IsActiveUnder(img.transform, parts.Handle.transform) || !img.enabled) continue;
                bool shape = SurfaceLook.IsShape(img.sprite);
                if (shape && parts.HandleInner == null) parts.HandleInner = img;
                else if (!shape && img.sprite != null && parts.Grip == null) parts.Grip = img;
            }

        foreach (Image img in card.GetComponentsInChildren<Image>(true))
        {
            if (InHandle(img.transform, parts) || !IsVisible(img) || Named(img.transform, SkippedNames)) continue;
            if (System.Array.IndexOf(BodyNames, img.name.ToLowerInvariant()) >= 0) { parts.Body = img; break; }
        }
        if (parts.Body == null && card.TryGetComponent(out Image own) && IsVisible(own)) parts.Body = own;

        TMP_Text firstText = null, byParent = null;
        foreach (TMP_Text text in card.GetComponentsInChildren<TMP_Text>(true))
        {
            if (InHandle(text.transform, parts)) continue;
            if (text.name.ToLowerInvariant() == "label") { parts.Title = text; break; }
            if (byParent == null && text.transform.parent != null && text.transform.parent.name.ToLowerInvariant().Contains("name")) byParent = text;
            if (firstText == null) firstText = text;
        }
        if (parts.Title == null) parts.Title = byParent != null ? byParent : firstText;
        return parts;
    }

    private static bool InHandle(Transform t, DragCardParts parts) => parts.Handle != null && t.IsChildOf(parts.Handle.transform);

    private static bool Named(Transform t, string[] names)
    {
        string name = t.name.ToLowerInvariant();
        foreach (string n in names)
            if (name == n || name.Contains(n)) return true;
        return false;
    }
}
