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

    public UIButtonStyle Style { get => style; set { style = value; Apply(); } }
    public override UIComponentStyle ComponentStyle => style;

    public override void AssignStyle(UIComponentStyle value) =>
        Style = value == null || value is UIButtonStyle ? (UIButtonStyle)value
              : throw new System.ArgumentException($"{value.name} is not a button style.", nameof(value));
}
