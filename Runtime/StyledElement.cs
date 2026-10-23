using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a styled element's background, label, icon and size from a <see cref="UIComponentStyle"/>. The shared
/// pipeline is <see cref="Apply"/>; each kind subclasses this to say how it finds its parts and to add what only it
/// needs — <see cref="StyledSelectable"/> for clickable controls, <see cref="StyledSurface"/> for panels.
///
/// A subclass declares its style as a serialized field named <c>style</c>: the merge and remove tools repoint that
/// field by name, so one implementation serves every kind.
/// </summary>
public abstract class StyledElement : MonoBehaviour, ILayoutElement
{
    [Tooltip("The Image that draws the element's shape. Auto-detected when the component is added.")]
    [SerializeField] private Image background;
    [Tooltip("Auto-detected: the element's text — an input field's text, a dropdown's caption, else the first TMP text.")]
    [SerializeField] private TMP_Text label;
    [Tooltip("Optional. Tinted with the style's icon colour.")]
    [SerializeField] private Graphic icon;

    [Header("Per-instance override (rare one-offs)")]
    [Tooltip("Keep this element's own size, ignoring the style's and the theme's height and width.")]
    [SerializeField] private bool keepSize = false;

    /// <summary>The style driving this element, whatever its concrete type.</summary>
    public abstract UIComponentStyle ComponentStyle { get; }

    /// <summary>Sets the style and applies it. Throws if the style is for a different kind.</summary>
    public abstract void AssignStyle(UIComponentStyle style);

    public Image Background => background;
    public TMP_Text Label => label;
    public Graphic Icon => icon;
    public bool KeepSize => keepSize;

    // At runtime the style applies once, on wake, and never on re-enable: code that recolours an element (a selected
    // quiz option) keeps that colour when its panel reopens. In the editor it re-applies on every enable so the
    // Scene view always shows the current style.
    protected virtual void Awake()
    {
        if (!Application.isPlaying) return;
        // At runtime there are no prefab overrides to avoid, so an element a layout sizes gets its Layout Element here
        // even when no editor pass added one.
        if (NeedsLayoutElement()) gameObject.AddComponent<LayoutElement>();
        Apply();
    }

    protected virtual void OnEnable()
    {
        if (!Application.isPlaying) Apply();
    }

    protected virtual void Reset() => AutoDetect();

    /// <summary>Fills any unassigned background / label / icon slot from the hierarchy.</summary>
    public virtual void AutoDetect()
    {
        if (background == null) background = DetectBackground();
        if (label == null) label = DetectLabel();
        if (icon == null) icon = DetectIcon();
    }

    protected abstract Image DetectBackground();
    protected abstract TMP_Text DetectLabel();
    /// <summary>The image the style's icon colour tints. A subclass overrides it to keep its own parts out.</summary>
    protected abstract Graphic DetectIcon();

    /// <summary>Writes the style onto the element. Returns whether anything changed.</summary>
    public bool Apply()
    {
        UIComponentStyle style = ComponentStyle;
        if (style == null) return false;
        UITheme theme = style.Theme;

        bool changed = CurrentLook(style).ApplyTo(background, theme);
        changed |= ApplyStates(style);
        changed |= ApplyLabel(style, theme);
        changed |= ApplyLabelPadding(style);
        if (icon != null && style.IconColor.TryResolve(theme, out Color tint) && icon.color != tint) { icon.color = tint; changed = true; }
        if (!keepSize) changed |= ApplySize(style);
        changed |= ApplyExtras(style);
        return changed;
    }

    /// <summary>
    /// Objects this kind writes beyond the shared parts (background, label, icon), so the editor saves them too —
    /// e.g. a toggle's checkmark and box.
    /// </summary>
    public virtual System.Collections.Generic.IEnumerable<Object> WrittenParts() { yield break; }

    /// <summary>How the element reacts to hover, press and selection; only clickable controls have any.</summary>
    protected virtual bool ApplyStates(UIComponentStyle style) => false;

    /// <summary>What only this kind needs, after the shared steps. Returns whether anything changed.</summary>
    protected virtual bool ApplyExtras(UIComponentStyle style) => false;

    /// <summary>The background look to show now. An element with states (a toggle's On/Off) picks by state.</summary>
    protected virtual SurfaceLook CurrentLook(UIComponentStyle style) => style.Background;

    /// <summary>The label colour to show now. An element with states picks by state.</summary>
    protected virtual ThemeColor CurrentLabelColour(UIComponentStyle style) => style.LabelColor;

    /// <summary>Writes the current label colour, if the style sets one. Returns whether anything changed.</summary>
    protected bool ApplyLabelColour(UIComponentStyle style)
    {
        if (label == null || !CurrentLabelColour(style).TryResolve(style.Theme, out Color ink) || label.color == ink) return false;
        label.color = ink;
        return true;
    }

    private bool ApplyLabel(UIComponentStyle style, UITheme theme)
    {
        if (label == null) return false;
        bool changed = ApplyLabelColour(style);
        float? size = style.LabelSize > 0f ? style.LabelSize : (float?)null;
        if (style.TryGetLabelRoleId(out string role))
        {
            if (label.TryGetComponent(out StyledText styled))
            {
                if (styled.RoleId != role) { styled.RoleId = role; changed = true; }
                if (styled.SizeOverride != size) { styled.OverrideSize(size); changed = true; }
            }
            else if (theme != null && theme.Typography != null)
            {
                changed |= TypographyScale.ApplyRole(label, theme.Typography.Get(role), size);
            }
        }
        else if (size.HasValue)
        {
            if (label.enableAutoSizing) { label.enableAutoSizing = false; changed = true; }
            if (!Mathf.Approximately(label.fontSize, size.Value)) { label.fontSize = size.Value; changed = true; }
        }
        return changed;
    }

    // An element with its own layout group positions the label itself, so the inset belongs in the group's padding;
    // offsetting the label's rect there would be overwritten on the next layout pass.
    private bool ApplyLabelPadding(UIComponentStyle style)
    {
        int pad = Mathf.RoundToInt(style.LabelPadding);
        if (pad < 0) return false;

        if (TryGetComponent(out HorizontalOrVerticalLayoutGroup group))
        {
            RectOffset p = group.padding;
            if (p.left == pad && p.right == pad) return false;
            group.padding = new RectOffset(pad, pad, p.top, p.bottom);
            return true;
        }

        if (label == null) return false;
        RectTransform rt = label.rectTransform;
        if (rt.anchorMin.x != 0f || rt.anchorMax.x != 1f) return false;
        if (Mathf.Approximately(rt.offsetMin.x, pad) && Mathf.Approximately(rt.offsetMax.x, -pad)) return false;
        rt.offsetMin = new Vector2(pad, rt.offsetMin.y);
        rt.offsetMax = new Vector2(-pad, rt.offsetMax.y);
        return true;
    }

    /// <summary>
    /// Sizes the element through whichever channel actually controls it. A LayoutElement outranks a LayoutGroup's
    /// own sizes, so writing it works under a parent layout group and under the element's own content size fitter
    /// alike; a free-standing element has its rect resized instead. A stretched axis is left alone, and a grid
    /// sizes its cells itself.
    /// </summary>
    private bool ApplySize(UIComponentStyle style)
    {
        var rt = (RectTransform)transform;
        TryGetComponent(out LayoutElement element);
        bool changed = false;

        float h = style.Height;
        if (h > 0f && !IsStretched(rt, vertical: true))
        {
            // Growing leaves the preferred height unset, so the element's own layout group reports its content's height.
            float preferred = style.GrowWithContent ? -1f : h;
            if (element != null && !Mathf.Approximately(element.preferredHeight, preferred)) { element.preferredHeight = preferred; changed = true; }
            if (element != null && !Mathf.Approximately(element.minHeight, h)) { element.minHeight = h; changed = true; }
            float target = style.GrowWithContent ? Mathf.Max(rt.sizeDelta.y, h) : h;
            if (!IsDriven(rt, vertical: true) && !Mathf.Approximately(rt.sizeDelta.y, target))
            {
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, target);
                changed = true;
            }
        }

        float w = style.Width;
        float minW = style.MinWidth;
        if ((w > 0f || minW > 0f) && !IsStretched(rt, vertical: false))
        {
            if (element != null && w > 0f && !Mathf.Approximately(element.preferredWidth, w)) { element.preferredWidth = w; changed = true; }
            if (element != null && minW > 0f && !Mathf.Approximately(element.minWidth, minW)) { element.minWidth = minW; changed = true; }
            if (!IsDriven(rt, vertical: false))
            {
                float target = w > 0f ? Mathf.Max(w, minW) : Mathf.Max(rt.sizeDelta.x, minW);
                if (!Mathf.Approximately(rt.sizeDelta.x, target))
                {
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, target);
                    changed = true;
                }
            }
        }
        return changed;
    }

    // ---- Growing with text ----

    // A sliced background Image reports its corner borders as a preferred height, often taller than the text needs.
    // For a style that grows with its text, the element's own layout group — its content — decides alone: this
    // outranks the Image and the LayoutElement, whose minimum still holds.
    float ILayoutElement.preferredHeight
    {
        get
        {
            UIComponentStyle style = ComponentStyle;
            if (style == null || !style.GrowWithContent || keepSize || !TryGetComponent(out LayoutGroup content)) return -1f;
            return Mathf.Max(style.Height, content.preferredHeight);
        }
    }

    int ILayoutElement.layoutPriority => 2;
    float ILayoutElement.minWidth => -1f;
    float ILayoutElement.preferredWidth => -1f;
    float ILayoutElement.flexibleWidth => -1f;
    float ILayoutElement.minHeight => -1f;
    float ILayoutElement.flexibleHeight => -1f;
    void ILayoutElement.CalculateLayoutInputHorizontal() { }
    void ILayoutElement.CalculateLayoutInputVertical() { }

    /// <summary>
    /// Whether the style sizes this element through a layout but it has no LayoutElement to carry the size yet.
    /// Adding one is left to the editor tools: doing it on every load would give each prefab instance an override.
    /// </summary>
    public bool NeedsLayoutElement()
    {
        UIComponentStyle style = ComponentStyle;
        if (style == null || !style.ControlsSize || TryGetComponent(out LayoutElement _)) return false;
        var rt = (RectTransform)transform;
        if (rt.parent != null && rt.parent.TryGetComponent(out GridLayoutGroup _)) return false;
        bool height = style.Height > 0f && !IsStretched(rt, true) && IsDriven(rt, true);
        bool width = (style.Width > 0f || style.MinWidth > 0f) && !IsStretched(rt, false) && IsDriven(rt, false);
        return height || width;
    }

    // ---- Shared lookups, also used by the editor tools ----

    protected static bool IsVisible(Image img) => img.enabled && img.color.a > 0.01f;

    // activeInHierarchy is false for every object in an unopened prefab asset, so walk activeSelf instead.
    protected static bool IsActiveUnder(Transform t, Transform root)
    {
        for (; t != null && t != root; t = t.parent)
            if (!t.gameObject.activeSelf) return false;
        return true;
    }

    public static bool IsStretched(RectTransform rt, bool vertical) =>
        vertical ? !Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y) : !Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x);

    // A parent layout group or own size fitter overwrites the rect every layout pass, so writing it would only
    // churn the asset; the LayoutElement is the channel those layouts read.
    public static bool IsDriven(RectTransform rt, bool vertical)
    {
        if (rt.TryGetComponent(out ContentSizeFitter fitter)
            && (vertical ? fitter.verticalFit : fitter.horizontalFit) != ContentSizeFitter.FitMode.Unconstrained)
            return true;
        if (rt.parent == null) return false;
        if (rt.parent.TryGetComponent(out GridLayoutGroup _)) return true;
        return rt.parent.TryGetComponent(out HorizontalOrVerticalLayoutGroup group)
               && (vertical ? group.childControlHeight : group.childControlWidth);
    }
}
