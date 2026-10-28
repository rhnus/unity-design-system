using UnityEngine;

/// <summary>
/// Drives a Button from a <see cref="UIButtonStyle"/>. Also works on toggles, input fields and dropdowns, whose
/// checkmark is the icon and whose text or caption is the label. The pipeline is shared with every styled control
/// and lives on <see cref="StyledSelectable"/>.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class StyledButton : StyledSelectable
{
    // Typed to the button style so the Inspector only offers button styles; named "style" per StyledSelectable.
    [SerializeField] private UIButtonStyle style;

    private bool showingDisabled;

    public UIButtonStyle Style { get => style; set { style = value; Apply(); } }
    public override UIComponentStyle ComponentStyle => style;

    public override void AssignStyle(UIComponentStyle value) =>
        Style = value == null || value is UIButtonStyle ? (UIButtonStyle)value
              : throw new System.ArgumentException($"{value.name} is not a button style.", nameof(value));

    /// <summary>Whether the button is showing its style's disabled look now.</summary>
    public bool ShowsDisabledLook => showingDisabled;

    /// <summary>
    /// Shows the disabled look outside play as well, for tests. Off, the editor never shows it, because a button
    /// saved non-interactable would write the grey into its prefab.
    /// </summary>
    public static bool DisabledLookInEditor { get; set; }

    private bool WantsDisabledLook =>
        (Application.isPlaying || DisabledLookInEditor) && style != null && style.UsesDisabledLook
        && Selectable != null && !Selectable.IsInteractable();

    protected override SurfaceLook CurrentLook(UIComponentStyle s) =>
        WantsDisabledLook ? s.Background.With(fill: style.DisabledFill) : s.Background;

    protected override ThemeColor CurrentLabelColour(UIComponentStyle s) =>
        WantsDisabledLook ? style.DisabledLabelColor : s.LabelColor;

    protected override bool ApplyExtras(UIComponentStyle s)
    {
        showingDisabled = WantsDisabledLook;
        return false;
    }

    // Polled: a Selectable raises nothing when it, or a CanvasGroup above it, stops being interactable. Re-applies
    // only the fill and label, so a size or font a script changed since wake is left alone.
    private void Update()
    {
        if (WantsDisabledLook == showingDisabled) return;
        showingDisabled = WantsDisabledLook;
        CurrentLook(style).ApplyTo(Background, style.Theme);
        ApplyLabelColour(style);
    }
}
