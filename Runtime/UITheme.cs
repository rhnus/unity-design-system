using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The design system's tokens — the one place a colour or a shape sprite is defined. Styles and components refer
/// to a token by its stable id, never by value, so renaming a token or re-importing it from Figma never breaks a
/// reference, and editing a value restyles every consumer.
///
/// Duplicate the asset to make a second brand or a dark mode: the copy keeps the same ids, so pointing a style
/// at the copy swaps every colour and shape at once.
/// </summary>
[CreateAssetMenu(fileName = "UITheme", menuName = DesignSystemNames.AssetMenuRoot + "UI Theme")]
public class UITheme : ScriptableObject
{
    [Serializable]
    public class ColorToken
    {
        [HideInInspector] public string id;

        [Tooltip("Slash-separated path, e.g. Brand/Primary. Matches the Figma variable name on import.")]
        public string name;

        public Color value = Color.white;
    }

    /// <summary>
    /// A named control size — Regular, Small, XSmall — so styles share a few heights instead of each picking pixels.
    /// A size can also set the text role its labels use, so small buttons get small text without each style saying so.
    /// </summary>
    [Serializable]
    public class SizeToken
    {
        [HideInInspector] public string id;

        [Tooltip("The size's name as styles show it, e.g. Small.")]
        public string name;

        [Min(0f)] public float height;

        [Tooltip("On = labels at this size use the role below, when styles use the theme font. Off = the theme's button text role.")]
        public bool setLabelRole;
        [HideInInspector] public TextRole labelRole = TextRole.Button;
        [TextRoleId(nameof(labelRole))] public string labelRoleId = "";

        public string LabelRoleId => TextRoles.Resolve(labelRoleId, labelRole);
    }

    /// <summary>A shape — usually a sliced rounded rect — referenced by every style that draws it.</summary>
    [Serializable]
    public class SpriteToken
    {
        [HideInInspector] public string id;

        [Tooltip("Slash-separated path, e.g. Shape/Button.")]
        public string name;

        public Sprite sprite;

        [Tooltip("The corner radius drawn into the sprite, in the sprite's own pixels. 0 = its left slice border, " +
                 "which is right for most rounded-rect sprites; set it when the border is cut wider than the corner.")]
        [Min(0f)] public float sourceCornerRadius;

        /// <summary>Recolours this sprite's baked-in colour to a style's fill. Generated and kept up to date by the editor.</summary>
        [HideInInspector] public Material recolorMaterial;

        /// <summary>The sprite's corner radius in its own pixels, or 0 when unknown.</summary>
        public float SourceCornerRadius => sourceCornerRadius > 0f ? sourceCornerRadius : sprite != null ? sprite.border.x : 0f;
    }

    [SerializeField] private List<ColorToken> colors = new List<ColorToken>();
    [SerializeField] private List<SpriteToken> sprites = new List<SpriteToken>();
    [Tooltip("Named control sizes. Styles pick one instead of a pixel height, so every Small button is the same Small.")]
    [SerializeField] private List<SizeToken> sizes = new List<SizeToken>();

    [Header("Buttons")]
    [Tooltip("Height of every button whose style uses the theme height. 0 = leave buttons at their own height.")]
    [SerializeField, Min(0f)] private float buttonHeight = 0f;

    [Tooltip("Give every button whose style uses the theme font the same text role, so one size change in the type " +
             "scale resizes them all. Off = each label keeps its own font.")]
    [SerializeField] private bool setButtonLabelFont = false;
    [SerializeField, HideInInspector] private TextRole buttonLabelRole = TextRole.Button;
    [TextRoleId(nameof(buttonLabelRole))]
    [SerializeField] private string buttonLabelRoleId = "";
    [Tooltip("The type scale button labels take their font from. Auto-filled in the editor.")]
    [SerializeField] private TypographyScale typography;

    [Header("Right / wrong")]
    [Tooltip("Every graded answer — quiz options, option cards, drag cards — takes the look below. Off = each keeps its own colours.")]
    [SerializeField] private bool useForGradedAnswers = false;
    [Tooltip("Fill of a right answer: the option's background, overlay or check fill.")]
    [SerializeField] private ThemeColor rightFill;
    [Tooltip("Border of a right answer: its outline or check disc. Empty = the fill colour.")]
    [SerializeField] private ThemeColor rightBorder;
    [Tooltip("The tick shown in a right answer's selection box. Empty = each option keeps its own mark.")]
    [SerializeField] private Sprite rightMark;
    [Tooltip("Fill of a wrong answer.")]
    [SerializeField] private ThemeColor wrongFill;
    [Tooltip("Border of a wrong answer. Empty = the fill colour.")]
    [SerializeField] private ThemeColor wrongBorder;
    [Tooltip("The cross shown in a wrong answer's selection box. Empty = each option keeps its own mark.")]
    [SerializeField] private Sprite wrongMark;

    /// <summary>
    /// The theme components read the right / wrong look from: the first one loaded. The game has one theme, and it
    /// loads with the first styled control in a scene, so nothing has to point at it.
    /// </summary>
    public static UITheme Active { get; set; }

    public bool UseForGradedAnswers { get => useForGradedAnswers; set => useForGradedAnswers = value; }
    public ThemeColor RightFill { get => rightFill; set => rightFill = value; }
    public ThemeColor RightBorder { get => rightBorder; set => rightBorder = value; }
    public Sprite RightMark { get => rightMark; set => rightMark = value; }
    public ThemeColor WrongFill { get => wrongFill; set => wrongFill = value; }
    public ThemeColor WrongBorder { get => wrongBorder; set => wrongBorder = value; }
    public Sprite WrongMark { get => wrongMark; set => wrongMark = value; }

    /// <summary>The look for a graded answer, or false when the theme leaves right / wrong to each component.</summary>
    public bool TryGetAnswerLook(bool correct, out AnswerLook look)
    {
        look = default;
        if (!useForGradedAnswers || !(correct ? rightFill : wrongFill).TryResolve(this, out Color fill)) return false;
        look.Fill = fill;
        look.Border = (correct ? rightBorder : wrongBorder).TryResolve(this, out Color border) ? border : fill;
        look.Mark = correct ? rightMark : wrongMark;
        return true;
    }

    public float ButtonHeight { get => buttonHeight; set => buttonHeight = value; }
    public bool SetButtonLabelFont { get => setButtonLabelFont; set => setButtonLabelFont = value; }
    public string ButtonLabelRoleId
    {
        get => TextRoles.Resolve(buttonLabelRoleId, buttonLabelRole);
        set
        {
            buttonLabelRoleId = value ?? "";
            if (TextRoles.TryGetBuiltIn(buttonLabelRoleId, out TextRole builtIn)) buttonLabelRole = builtIn;
        }
    }

    public TextRole ButtonLabelRole
    {
        get => TextRoles.TryGetBuiltIn(ButtonLabelRoleId, out TextRole r) ? r : buttonLabelRole;
        set => ButtonLabelRoleId = value.ToString();
    }
    public TypographyScale Typography { get => typography; set => typography = value; }

    private Dictionary<string, ColorToken> colorsById;
    private Dictionary<string, SpriteToken> spritesById;

    public IReadOnlyList<ColorToken> Colors => colors;
    public IReadOnlyList<SpriteToken> Sprites => sprites;
    public IReadOnlyList<SizeToken> Sizes => sizes;

    public SizeToken GetSize(string id) => string.IsNullOrEmpty(id) ? null : sizes.Find(s => s != null && s.id == id);

    /// <summary>The size with this name, added at this height when the theme has none by that name.</summary>
    public SizeToken GetOrAddSize(string sizeName, float height)
    {
        SizeToken size = sizes.Find(s => s != null && s.name == sizeName);
        if (size != null) return size;
        size = new SizeToken { id = NewId(), name = sizeName, height = height };
        sizes.Add(size);
        return size;
    }

    public bool TryGetColor(string id, out Color color)
    {
        ColorToken token = GetToken(id);
        color = token != null ? token.value : Color.magenta;
        return token != null;
    }

    public ColorToken GetToken(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (colorsById == null || colorsById.Count != colors.Count) RebuildLookup();
        return colorsById.TryGetValue(id, out ColorToken token) ? token : null;
    }

    public SpriteToken GetSprite(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (spritesById == null || spritesById.Count != sprites.Count) RebuildLookup();
        return spritesById.TryGetValue(id, out SpriteToken token) ? token : null;
    }

    public ColorToken FindByName(string tokenName) =>
        colors.Find(t => t != null && string.Equals(t.name, tokenName, StringComparison.OrdinalIgnoreCase));

    public SpriteToken FindSprite(Sprite sprite) => sprite == null ? null : sprites.Find(t => t != null && t.sprite == sprite);

    /// <summary>Adds a token, or updates the value of the one already carrying this name.</summary>
    public ColorToken SetColor(string tokenName, Color value)
    {
        ColorToken token = FindByName(tokenName);
        if (token == null)
        {
            token = new ColorToken { id = NewId(), name = tokenName };
            colors.Add(token);
        }
        token.value = value;
        RebuildLookup();
        return token;
    }

    /// <summary>Removes a colour token. Callers make sure nothing refers to it any more.</summary>
    public bool RemoveColor(string id)
    {
        bool removed = colors.RemoveAll(t => t != null && t.id == id) > 0;
        if (removed) RebuildLookup();
        return removed;
    }

    /// <summary>The token already holding this sprite, or a new one under the given name.</summary>
    public SpriteToken GetOrAddSprite(string tokenName, Sprite sprite)
    {
        SpriteToken token = FindSprite(sprite);
        if (token != null) return token;
        token = new SpriteToken { id = NewId(), name = tokenName, sprite = sprite };
        sprites.Add(token);
        RebuildLookup();
        return token;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private void RebuildLookup()
    {
        colorsById = new Dictionary<string, ColorToken>();
        foreach (ColorToken t in colors)
            if (t != null && !string.IsNullOrEmpty(t.id)) colorsById[t.id] = t;
        spritesById = new Dictionary<string, SpriteToken>();
        foreach (SpriteToken t in sprites)
            if (t != null && !string.IsNullOrEmpty(t.id)) spritesById[t.id] = t;
    }

    private void OnEnable()
    {
        RebuildLookup();
        if (Active == null) Active = this;
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
    }

    // Duplicating a list element in the Inspector copies its id, so every id after the first is reissued —
    // otherwise two tokens would answer to one reference.
    private void OnValidate()
    {
        var seen = new HashSet<string>();
        foreach (ColorToken t in colors)
            if (t != null && (string.IsNullOrEmpty(t.id) || !seen.Add(t.id))) seen.Add(t.id = NewId());
        foreach (SpriteToken t in sprites)
            if (t != null && (string.IsNullOrEmpty(t.id) || !seen.Add(t.id))) seen.Add(t.id = NewId());
        foreach (SizeToken t in sizes)
            if (t != null && (string.IsNullOrEmpty(t.id) || !seen.Add(t.id))) seen.Add(t.id = NewId());
        RebuildLookup();
    }
}

/// <summary>A reference to one <see cref="UITheme"/> colour token. Drawn as a named dropdown in the Inspector.</summary>
[Serializable]
public struct ThemeColor
{
    public string tokenId;

    public bool IsSet => !string.IsNullOrEmpty(tokenId);

    public bool TryResolve(UITheme theme, out Color color)
    {
        color = Color.white;
        return IsSet && theme != null && theme.TryGetColor(tokenId, out color);
    }

    public static ThemeColor From(UITheme.ColorToken token) => new ThemeColor { tokenId = token?.id };
}

/// <summary>A reference to one <see cref="UITheme"/> sprite token. Drawn as a named dropdown in the Inspector.</summary>
[Serializable]
public struct ThemeSprite
{
    public string tokenId;

    public bool IsSet => !string.IsNullOrEmpty(tokenId);

    public UITheme.SpriteToken Resolve(UITheme theme) => IsSet && theme != null ? theme.GetSprite(tokenId) : null;

    public static ThemeSprite From(UITheme.SpriteToken token) => new ThemeSprite { tokenId = token?.id };
}

/// <summary>A reference to one <see cref="UITheme"/> named size. Drawn as a dropdown of the theme's size names.</summary>
[Serializable]
public struct ThemeSize
{
    public string tokenId;

    public bool IsSet => !string.IsNullOrEmpty(tokenId);

    public UITheme.SizeToken Resolve(UITheme theme) => IsSet && theme != null ? theme.GetSize(tokenId) : null;

    public static ThemeSize From(UITheme.SizeToken token) => new ThemeSize { tokenId = token?.id };
}
