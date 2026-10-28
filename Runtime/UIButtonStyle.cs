using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One button variant — Primary, Secondary, Ghost, Danger... Put a <see cref="StyledButton"/> on a Button and pick
/// a style; editing this asset restyles every button using it. Everything it holds is shared with other controls'
/// styles and lives on <see cref="UIComponentStyle"/>, except the disabled look, which only buttons have.
/// </summary>
[CreateAssetMenu(fileName = "ButtonStyle", menuName = DesignSystemNames.AssetMenuRoot + "Button Style")]
public class UIButtonStyle : UIComponentStyle
{
    [Header("Disabled look")]
    [Tooltip("On: a button that can't be used swaps to the fill and label colours below, in play. Off: it is " +
             "dimmed by the Disabled Tint, which can only darken, so a solid-colour button can never turn light grey.")]
    [SerializeField] private bool useDisabledLook;
    [SerializeField] private ThemeColor disabledFill;
    [SerializeField] private ThemeColor disabledLabelColor;

    public bool UsesDisabledLook { get => useDisabledLook; set => useDisabledLook = value; }
    public ThemeColor DisabledFill { get => disabledFill; set => disabledFill = value; }
    public ThemeColor DisabledLabelColor { get => disabledLabelColor; set => disabledLabelColor = value; }

    // The disabled look already is the disabled colour; multiplying the tint over it as well would fade it.
    public override ColorBlock StateColors
    {
        get
        {
            ColorBlock tints = base.StateColors;
            if (useDisabledLook) tints.disabledColor = Color.white;
            return tints;
        }
    }
}
