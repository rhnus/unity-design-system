using UnityEngine;

/// <summary>
/// The shared look of a draggable card: the orange handle with its grip icon, then the body. The shared Look is
/// the body; the handle has its own. What a card displays — an ID chip, a duration line, a count — stays the card's
/// own, so one style can serve cards that show different information. Put a <see cref="StyledDragCard"/> on the
/// card to apply it. A new drag card style leaves height, label font and hover alone.
/// </summary>
[CreateAssetMenu(fileName = "DragCardStyle", menuName = DesignSystemNames.AssetMenuRoot + "Drag Card Style")]
public class UIDragCardStyle : UIComponentStyle
{
    [Tooltip("The handle's frame: shape, colour and corner. Empty fill and sprite = keep each card's own handle.")]
    [SerializeField] private SurfaceLook handle = new SurfaceLook();
    [Tooltip("Colour of the handle's inner panel, on cards drawn with a two-tone handle. Empty = keep each card's own.")]
    [SerializeField] private ThemeColor handleInnerFill;
    [Tooltip("Colour of the grip icon. Empty = keep each card's own.")]
    [SerializeField] private ThemeColor gripColor;
    [Tooltip("The grip icon's sprite. Empty = keep each card's own. Also the icon a handle is built with on a card that has none.")]
    [SerializeField] private Sprite gripSprite;
    [Tooltip("Handle width in px. 0 = keep each card's own.")]
    [SerializeField, Min(0f)] private float handleWidth = 0f;


    public SurfaceLook Handle => handle;
    public ThemeColor HandleInnerFill { get => handleInnerFill; set => handleInnerFill = value; }
    public ThemeColor GripColor { get => gripColor; set => gripColor = value; }
    public float HandleWidth { get => handleWidth; set => handleWidth = value; }
    public Sprite GripSprite { get => gripSprite; set => gripSprite = value; }

    /// <summary>Whether the style sets the handle's look at all; an empty one leaves every card's handle alone.</summary>
    public bool SetsHandle => handle.fill.IsSet || handle.shape.IsSet || handle.sprite != null;

    protected override void OnFirstEnable()
    {
        HeightSource = ButtonHeightSource.KeepAuthored;
        LabelFontSource = LabelFontSource.KeepAuthored;
        ApplyStateTints = false;
    }
}
