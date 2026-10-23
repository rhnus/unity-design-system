using UnityEngine;

/// <summary>
/// One button variant — Primary, Secondary, Ghost, Danger... Put a <see cref="StyledButton"/> on a Button and pick
/// a style; editing this asset restyles every button using it. Everything it holds is shared with other controls'
/// styles and lives on <see cref="UIComponentStyle"/>.
/// </summary>
[CreateAssetMenu(fileName = "ButtonStyle", menuName = DesignSystemNames.AssetMenuRoot + "Button Style")]
public class UIButtonStyle : UIComponentStyle
{
}
