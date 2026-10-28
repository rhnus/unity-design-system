using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>Where a <see cref="UIComponentStyle"/> takes its label font from.</summary>
public enum LabelFontSource
{
    [InspectorName("Use theme font")] Theme,
    [InspectorName("Custom role")] Custom,
    [InspectorName("Keep each label's own")] KeepAuthored,
}

/// <summary>Where a <see cref="UIComponentStyle"/> takes its height from.</summary>
public enum ButtonHeightSource
{
    [InspectorName("Use theme height")] Theme,
    [InspectorName("Custom")] Custom,
    [InspectorName("Keep each control's own")] KeepAuthored,
    [InspectorName("Named size")] NamedSize,
}

/// <summary>
/// What every styled element's style has in common — background look, label, icon, hover and press tints, size,
/// and variant bookkeeping. Each kind (button, toggle, panel…) subclasses it to add what only it has, so the theme,
/// the tools and the variant system work on all of them through this one type. A kind without labels or states
/// (panels) leaves those fields off and its Inspector hides them.
/// </summary>
public abstract class UIComponentStyle : ScriptableObject
{
    // A variant copies its base's values in the editor (see VariantSync) except the fields listed in overrides, so
    // at runtime it is an ordinary, self-contained style and nothing resolves through the base.
    [SerializeField, HideInInspector] private UIComponentStyle baseStyle;
    [SerializeField, HideInInspector] private List<string> overrides = new List<string>();

    [SerializeField] private UITheme theme;

    [FormerlySerializedAs("look")]
    [SerializeField] private SurfaceLook background = new SurfaceLook();

    [SerializeField] private ThemeColor labelColor;
    [Tooltip("Use theme font: the theme's button text role, so one size in the type scale sizes every label. " +
             "Custom role: the role below. Keep each label's own: never touch the font.")]
    [SerializeField] private LabelFontSource labelFontSource = LabelFontSource.Theme;
    [Tooltip("Used when the label font is Custom.")]
    [SerializeField, HideInInspector] private TextRole labelRole = TextRole.Button;
    [TextRoleId(nameof(labelRole))]
    [SerializeField] private string labelRoleId = "";
    [Tooltip("Text size for this style's labels, on top of the role's font. 0 = the role's size.")]
    [SerializeField, Min(0f)] private float labelSize = 0f;
    [SerializeField, HideInInspector] private bool applyLabelRole = false;
    [Tooltip("Left/right space between the label and the control's edges. Goes to the control's own layout group " +
             "when it has one, otherwise to a stretch-anchored label's rect. -1 = leave as authored.")]
    [SerializeField] private float labelPadding = -1f;

    [SerializeField] private ThemeColor iconColor;

    // The fill lives on the Image and these tints multiply over it, so code that resets a control's ColorBlock to
    // Unity's default (the tutorial nodes do) leaves the control's colour intact.
    [Tooltip("Off = leave each control's transition alone, e.g. controls that use Sprite Swap.")]
    [SerializeField] private bool applyStateTints = true;
    [SerializeField] private Color highlightedTint = new Color(0.94f, 0.94f, 0.94f, 1f);
    [SerializeField] private Color pressedTint = new Color(0.82f, 0.82f, 0.82f, 1f);
    [SerializeField] private Color selectedTint = new Color(0.94f, 0.94f, 0.94f, 1f);
    [SerializeField] private Color disabledTint = new Color(0.78f, 0.78f, 0.78f, 0.5f);
    [SerializeField, Min(0f)] private float fadeDuration = 0.1f;

    [Tooltip("Use theme height: the theme's Button Height, so one number sizes every control. Custom: the height below. " +
             "Keep each control's own: never touch the height. Heights work inside layout groups and content size " +
             "fitters too; a control stretched vertically by its anchors keeps its parent's height.")]
    [SerializeField] private ButtonHeightSource heightSource = ButtonHeightSource.Theme;
    [Tooltip("Used when Height is Custom.")]
    [SerializeField, Min(0f)] private float height = 0f;
    [Tooltip("Used when Height is Named size: one of the theme's sizes, e.g. Small.")]
    [SerializeField] private ThemeSize size;
    [Tooltip("On = the height is a minimum: a control whose text needs more room grows to show it, inside a layout. " +
             "For option cards and other controls that carry sentences rather than a word or two.")]
    [SerializeField] private bool growWithContent;
    [Tooltip("Width, applied the same way as height. 0 = leave as authored, e.g. controls that size to their label.")]
    [SerializeField, Min(0f)] private float width = 0f;
    [Tooltip("Widens a narrower control; never shrinks one. Useful with controls that size to their label. 0 = off.")]
    [SerializeField, Min(0f)] private float minWidth = 0f;
    [Tooltip("Make the width equal the height — for icon-only controls.")]
    [SerializeField] private bool square = false;

    // Assets saved before the theme-driven fields existed read them as Theme. Settings such an asset already had
    // were deliberate, so they are carried over once on load: a typed height becomes a Custom height, and a label
    // role that was switched on becomes a Custom role.
    [SerializeField, HideInInspector] private int dataVersion;
    [SerializeField, HideInInspector] private bool locked;

    protected virtual void OnEnable()
    {
        bool fresh = dataVersion == 0;
        if (dataVersion < 1 && height > 0f) heightSource = ButtonHeightSource.Custom;
        if (dataVersion < 2 && applyLabelRole) labelFontSource = LabelFontSource.Custom;
        dataVersion = 2;
        if (fresh) OnFirstEnable();
    }

    /// <summary>
    /// Defaults for a style that has never been saved. Button styles have none: an unsaved-looking button asset may
    /// predate this versioning, and its values must be kept. A newer kind sets its own starting values here.
    /// </summary>
    protected virtual void OnFirstEnable() { }

    public UITheme Theme { get => theme; set => theme = value; }
    public UIComponentStyle BaseStyle { get => baseStyle; set => baseStyle = value; }
    public bool IsVariant => baseStyle != null;
    public List<string> Overrides => overrides;
    public float LabelSize { get => labelSize; set => labelSize = value; }
    public bool Square { get => square; set => square = value; }
    public SurfaceLook Background => background;
    public ThemeColor LabelColor { get => labelColor; set => labelColor = value; }

    /// <summary>Whether controls of this style look different when on (toggles), so previews show both states.</summary>
    public virtual bool HasOnState => false;
    /// <summary>The background look for a state; the same for both unless the style has an On state.</summary>
    public virtual SurfaceLook LookFor(bool on) => Background;
    /// <summary>The label colour for a state; the same for both unless the style has an On state.</summary>
    public virtual ThemeColor LabelColorFor(bool on) => LabelColor;
    public ThemeColor IconColor => iconColor;
    public LabelFontSource LabelFontSource { get => labelFontSource; set => labelFontSource = value; }

    /// <summary>An approved style the tools won't edit, merge away, remove or repair. Theme defaults it follows still apply.</summary>
    public bool Locked { get => locked; set => locked = value; }

    /// <summary>The text role id this style gives its labels, if it controls their font at all.</summary>
    public bool TryGetLabelRoleId(out string roleId)
    {
        roleId = TextRoles.Resolve(labelRoleId, labelRole);
        switch (labelFontSource)
        {
            case LabelFontSource.Theme when theme != null && theme.SetButtonLabelFont:
                UITheme.SizeToken named = heightSource == ButtonHeightSource.NamedSize ? size.Resolve(theme) : null;
                roleId = named != null && named.setLabelRole ? named.LabelRoleId : theme.ButtonLabelRoleId;
                return true;
            case LabelFontSource.Custom:
                return true;
            default:
                return false;
        }
    }

    /// <summary>The built-in form of <see cref="TryGetLabelRoleId"/>; a role of the project's own reads as the style's last built-in one.</summary>
    public bool TryGetLabelRole(out TextRole role)
    {
        bool controls = TryGetLabelRoleId(out string id);
        role = TextRoles.TryGetBuiltIn(id, out TextRole builtIn) ? builtIn : labelRole;
        return controls;
    }

    public bool ApplyLabelRole => TryGetLabelRoleId(out _);
    public string LabelRoleId => TryGetLabelRoleId(out string id) ? id : TextRoles.Resolve(labelRoleId, labelRole);
    public TextRole LabelRole => TryGetLabelRole(out TextRole role) ? role : labelRole;

    /// <summary>Makes this style give its labels the given role, whatever the theme says.</summary>
    public void SetCustomLabelRole(TextRole role) => SetCustomLabelRole(role.ToString());

    /// <summary>Makes this style give its labels the given role id, whatever the theme says.</summary>
    public void SetCustomLabelRole(string roleId)
    {
        labelRoleId = roleId ?? "";
        if (TextRoles.TryGetBuiltIn(labelRoleId, out TextRole builtIn)) labelRole = builtIn;
        labelFontSource = LabelFontSource.Custom;
    }

    public float LabelPadding { get => labelPadding; set => labelPadding = value; }
    public bool ApplyStateTints { get => applyStateTints; set => applyStateTints = value; }
    public ButtonHeightSource HeightSource { get => heightSource; set => heightSource = value; }

    /// <summary>Whether <see cref="Height"/> is a minimum that a control with more text grows past.</summary>
    public bool GrowWithContent { get => growWithContent; set => growWithContent = value; }

    /// <summary>The theme size this style uses when <see cref="HeightSource"/> is a named size.</summary>
    public ThemeSize Size { get => size; set => size = value; }

    /// <summary>The named size this style uses, or null for a theme, custom or kept height.</summary>
    public UITheme.SizeToken NamedSize => heightSource == ButtonHeightSource.NamedSize ? size.Resolve(theme) : null;

    /// <summary>The height this style gives its controls, or 0 to leave them as authored. Setting it makes it Custom.</summary>
    public float Height
    {
        get => heightSource switch
        {
            ButtonHeightSource.Theme => theme != null ? theme.ButtonHeight : 0f,
            ButtonHeightSource.Custom => height,
            ButtonHeightSource.NamedSize => size.Resolve(theme)?.height ?? 0f,
            _ => 0f,
        };
        set
        {
            height = value;
            heightSource = ButtonHeightSource.Custom;
        }
    }

    /// <summary>The width this style gives its controls, or 0 to leave them as authored. A square style matches its height.</summary>
    public float Width { get => square && Height > 0f ? Height : width; set => width = value; }
    public float MinWidth { get => minWidth; set => minWidth = value; }
    public bool ControlsSize => Height > 0f || Width > 0f || minWidth > 0f;

    public virtual ColorBlock StateColors => new ColorBlock
    {
        normalColor = Color.white,
        highlightedColor = highlightedTint,
        pressedColor = pressedTint,
        selectedColor = selectedTint,
        disabledColor = disabledTint,
        colorMultiplier = 1f,
        fadeDuration = fadeDuration,
    };
}
