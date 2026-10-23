using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a Toggle from a <see cref="UIToggleStyle"/>: the shared pipeline plus its On/Off look, label colour,
/// checkmark and box size. When the value changes it updates only the state-dependent parts — background and label
/// colour — so size or checkmark set by game code is never touched mid-game.
///
/// The value can change without any event: game code clears the Toggle's listeners (QuizOptionView.Bind) or sets it
/// with SetIsOnWithoutNotify (single-select quizzes). So besides the event, <see cref="SyncState"/> compares the
/// value with the state last shown once per frame — a single bool comparison, and only for styles that look
/// different when on.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Toggle))]
public class StyledToggle : StyledSelectable
{
    // Typed to the toggle style so the Inspector only offers toggle styles; named "style" per StyledSelectable.
    [SerializeField] private UIToggleStyle style;

    [Tooltip("The checkmark image. Auto-detected from the Toggle's Graphic.")]
    [SerializeField] private Image checkmark;

    private Toggle toggle;
    private bool shownOn;

    public UIToggleStyle Style { get => style; set { style = value; Apply(); } }
    public override UIComponentStyle ComponentStyle => style;
    public Image Checkmark => checkmark;

    public override void AssignStyle(UIComponentStyle value) =>
        Style = value == null || value is UIToggleStyle ? (UIToggleStyle)value
              : throw new System.ArgumentException($"{value.name} is not a toggle style.", nameof(value));

    private Toggle Toggle => toggle != null ? toggle : toggle = GetComponent<Toggle>();
    private bool IsOn => Toggle != null && Toggle.isOn;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (Toggle != null) Toggle.onValueChanged.AddListener(OnValueChanged);
    }

    protected virtual void OnDisable()
    {
        if (Toggle != null) Toggle.onValueChanged.RemoveListener(OnValueChanged);
    }

    private void OnValueChanged(bool _) => ApplyState();

    private void LateUpdate() => SyncState();

    /// <summary>Shows the On or Off look if the value changed since it was last shown, however it changed.</summary>
    public void SyncState()
    {
        if (IsOn != shownOn && style != null && style.ChangesWhenOn) ApplyState();
    }

    /// <summary>Updates the parts that depend on On/Off. Returns whether anything changed.</summary>
    public bool ApplyState()
    {
        if (style == null) return false;
        shownOn = IsOn;
        bool changed = CurrentLook(style).ApplyTo(Background, style.Theme);
        changed |= ApplyLabelColour(style);
        return changed;
    }

    public override System.Collections.Generic.IEnumerable<Object> WrittenParts()
    {
        if (checkmark != null)
        {
            yield return checkmark;
            yield return checkmark.rectTransform;
        }
        if (Background != null) yield return Background.rectTransform;
    }

    public override void AutoDetect()
    {
        base.AutoDetect();
        if (checkmark == null && Toggle != null) checkmark = Toggle.graphic as Image;
    }

    // The checkmark has its own colour in the style, so it must not also be tinted as the icon.
    protected override Graphic DetectIcon() => FindIcon(Selectable, Background, Toggle != null ? Toggle.graphic : null);

    protected override SurfaceLook CurrentLook(UIComponentStyle s)
    {
        shownOn = IsOn;
        return style.LookFor(shownOn);
    }
    protected override ThemeColor CurrentLabelColour(UIComponentStyle s) => style.LabelColorFor(IsOn);

    protected override bool ApplyExtras(UIComponentStyle s)
    {
        bool changed = false;
        if (checkmark != null)
        {
            if (style.CheckSprite != null && checkmark.sprite != style.CheckSprite) { checkmark.sprite = style.CheckSprite; changed = true; }
            if (style.CheckColor.TryResolve(style.Theme, out Color tick) && checkmark.color != tick) { checkmark.color = tick; changed = true; }
            if (style.CheckSize > 0f) changed |= SetSquare(checkmark.rectTransform, style.CheckSize);
        }
        // Only a box that is a child of the row is sized here; when the toggle itself is the box, Height covers it.
        if (style.BoxSize > 0f && Background != null && Background.transform != transform)
            changed |= SetSquare(Background.rectTransform, style.BoxSize);
        return changed;
    }

    private static bool SetSquare(RectTransform rt, float size)
    {
        bool changed = false;
        foreach (bool vertical in new[] { false, true })
        {
            if (IsStretched(rt, vertical) || IsDriven(rt, vertical)) continue;
            float current = vertical ? rt.sizeDelta.y : rt.sizeDelta.x;
            if (Mathf.Approximately(current, size)) continue;
            rt.SetSizeWithCurrentAnchors(vertical ? RectTransform.Axis.Vertical : RectTransform.Axis.Horizontal, size);
            changed = true;
        }
        return changed;
    }
}
