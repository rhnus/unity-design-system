using UnityEngine;

/// <summary>The role a toggle plays, which decides its starting values and what the tools check for.</summary>
public enum ToggleType
{
    [InspectorName("Checkbox")] Checkbox,
    [InspectorName("Radio")] Radio,
    [InspectorName("Tab / segment")] Tab,
    [InspectorName("Option card")] Card,
}

/// <summary>
/// One toggle variant — a checkbox, a radio, a tab, an option card. The shared fields describe the Off state; the
/// On state only overrides what changes (fill, shape, label colour), so a designer never sets the shape twice.
/// Put a <see cref="StyledToggle"/> on a Toggle and pick it.
/// </summary>
[CreateAssetMenu(fileName = "ToggleStyle", menuName = DesignSystemNames.AssetMenuRoot + "Toggle Style")]
public class UIToggleStyle : UIComponentStyle
{
    [Tooltip("What this toggle is for. Sets sensible starting values and what the design check looks for.")]
    [SerializeField] private ToggleType toggleType = ToggleType.Checkbox;

    [Tooltip("Background colour while On. Empty = same as Off.")]
    [SerializeField] private ThemeColor onFill;
    [Tooltip("Background shape while On, e.g. a filled box for an outlined one. Empty = same as Off.")]
    [SerializeField] private ThemeSprite onShape;
    [Tooltip("Label colour while On. Empty = same as Off.")]
    [SerializeField] private ThemeColor onLabelColor;

    [Tooltip("Checkmark image. Empty = keep each toggle's own.")]
    [SerializeField] private Sprite checkSprite;
    [Tooltip("Checkmark colour. Empty = keep each toggle's own.")]
    [SerializeField] private ThemeColor checkColor;
    [Tooltip("Checkmark width and height in px. 0 = keep each toggle's own.")]
    [SerializeField, Min(0f)] private float checkSize = 0f;
    [Tooltip("Width and height of the box, when the box is a child of the toggle row (checkboxes, radios). 0 = keep.")]
    [SerializeField, Min(0f)] private float boxSize = 0f;

    public ToggleType ToggleType { get => toggleType; set => toggleType = value; }
    public ThemeColor OnFill { get => onFill; set => onFill = value; }
    public ThemeSprite OnShape { get => onShape; set => onShape = value; }
    public ThemeColor OnLabelColor { get => onLabelColor; set => onLabelColor = value; }
    public Sprite CheckSprite { get => checkSprite; set => checkSprite = value; }
    public ThemeColor CheckColor { get => checkColor; set => checkColor = value; }
    public float CheckSize { get => checkSize; set => checkSize = value; }
    public float BoxSize { get => boxSize; set => boxSize = value; }

    // A toggle is usually a row (box + label), not a button-height block, so a new toggle style leaves height alone
    // rather than following the theme's button height.
    protected override void OnFirstEnable() => HeightSource = ButtonHeightSource.KeepAuthored;

    public override bool HasOnState => true;

    /// <summary>Whether anything looks different while on, so a toggle has to follow its state at all.</summary>
    public bool ChangesWhenOn => onFill.IsSet || onShape.IsSet || onLabelColor.IsSet;

    /// <summary>The background look for a state: the Off look, with the On overrides when on.</summary>
    public override SurfaceLook LookFor(bool on)
    {
        if (!on || (!onFill.IsSet && !onShape.IsSet)) return Background;
        return Background.With(onFill.IsSet ? onFill : (ThemeColor?)null, onShape.IsSet ? onShape : (ThemeSprite?)null);
    }

    /// <summary>The label colour for a state: the Off colour, or the On colour when on and set.</summary>
    public override ThemeColor LabelColorFor(bool on) => on && onLabelColor.IsSet ? onLabelColor : LabelColor;
}
