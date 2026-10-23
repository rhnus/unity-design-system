using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>How a <see cref="SurfaceLook"/>'s fill colour meets its sprite.</summary>
public enum SurfaceFillMode
{
    /// <summary>Multiply the fill over the sprite. Right for white sprites.</summary>
    [InspectorName("Tint (white sprites)")] Tint,
    /// <summary>Replace the sprite's own colour with the fill, keeping its shading, shadow and alpha. Lets a sprite
    /// with colour baked in follow a colour token without new art. Needs a sprite token.</summary>
    [InspectorName("Recolor (coloured sprites)")] Recolor,
}

/// <summary>Shape and fill of one Image: sprite, slicing, corner radius and colour token.</summary>
[Serializable]
public class SurfaceLook
{
    /// <summary>Shader of the materials <see cref="SurfaceFillMode.Recolor"/> assigns.</summary>
    public const string RecolorShader = "UI/DesignSystem/Recolor";

    [Tooltip("Sprite token from the theme. Takes precedence over Sprite below, so one edit in the theme reshapes " +
             "every style drawing it.")]
    public ThemeSprite shape;

    [Tooltip("Used when no sprite token is set. Leave both empty to keep each Image's own sprite.")]
    public Sprite sprite;

    public Image.Type imageType = Image.Type.Sliced;
    public bool fillCenter = true;

    [Tooltip("Corner radius in canvas pixels — the number on the Figma frame. Converted to the Image's Pixels Per " +
             "Unit Multiplier from the sprite's corner, so it renders the same whatever the sprite's resolution. " +
             "0 = use the multiplier below as-is.")]
    [Min(0f)] public float cornerRadius = 0f;

    [Min(0.01f)] public float pixelsPerUnitMultiplier = 1f;

    [Tooltip("Colour token for the Image. Empty = leave the Image's colour alone.")]
    public ThemeColor fill;

    [Tooltip("Tint multiplies the fill over the sprite — for white sprites. Recolor replaces a baked-in sprite " +
             "colour with the fill and keeps its shading — for existing art that isn't white. Recolor needs a sprite token.")]
    public SurfaceFillMode fillMode = SurfaceFillMode.Tint;

    /// <summary>A copy with the fill and/or shape replaced — a state's look built on top of this one.</summary>
    public SurfaceLook With(ThemeColor? fill = null, ThemeSprite? shape = null)
    {
        var copy = (SurfaceLook)MemberwiseClone();
        if (fill.HasValue) copy.fill = fill.Value;
        if (shape.HasValue) copy.shape = shape.Value;
        return copy;
    }

    /// <summary>The sprite this look draws: the token's, else the plain field's, else null (keep the Image's).</summary>
    public Sprite ResolveSprite(UITheme theme)
    {
        UITheme.SpriteToken token = shape.Resolve(theme);
        return token != null && token.sprite != null ? token.sprite : sprite;
    }

    /// <summary>
    /// Whether this look recolours rather than tints: only for a sliced shape sprite whose token has a material.
    /// A picture — an icon, a character — has no single body colour, and recolouring flattens it to one tone.
    /// </summary>
    public bool Recolors(UITheme theme)
    {
        if (fillMode != SurfaceFillMode.Recolor) return false;
        UITheme.SpriteToken token = shape.Resolve(theme);
        return token != null && token.recolorMaterial != null && IsShape(token.sprite);
    }

    /// <summary>A sprite drawn as a resizable shape (it has slice borders), as opposed to a picture.</summary>
    public static bool IsShape(Sprite sprite) => sprite != null && sprite.border != Vector4.zero;

    /// <summary>Writes this look onto the image. Returns whether anything changed.</summary>
    public bool ApplyTo(Image image, UITheme theme)
    {
        if (image == null) return false;
        bool changed = false;

        Sprite s = ResolveSprite(theme);
        if (s != null && image.sprite != s) { image.sprite = s; changed = true; }
        if (image.type != imageType) { image.type = imageType; changed = true; }
        if (image.fillCenter != fillCenter) { image.fillCenter = fillCenter; changed = true; }

        float multiplier = MultiplierFor(image, theme);
        if (!Mathf.Approximately(image.pixelsPerUnitMultiplier, multiplier))
        {
            image.pixelsPerUnitMultiplier = multiplier;
            changed = true;
        }

        changed |= ApplyMaterial(image, theme);
        if (fill.TryResolve(theme, out Color c) && image.color != c) { image.color = c; changed = true; }
        return changed;
    }

    // Only a material this system assigned is ever removed, so an Image with its own custom material keeps it
    // under Tint.
    private bool ApplyMaterial(Image image, UITheme theme)
    {
        Material wanted = Recolors(theme) ? shape.Resolve(theme).recolorMaterial : null;
        Material current = image.material != image.defaultMaterial ? image.material : null;
        if (current == wanted) return false;
        if (wanted == null && (current == null || current.shader == null || current.shader.name != RecolorShader)) return false;
        image.material = wanted;
        return true;
    }

    /// <summary>
    /// The multiplier that renders the sprite's corner at <see cref="cornerRadius"/> canvas pixels. A sliced
    /// border is drawn at border / (pixelsPerUnit × multiplier) units; the sprite's own corner radius comes from
    /// its token, defaulting to the left slice border.
    /// </summary>
    public float MultiplierFor(Image image, UITheme theme = null)
    {
        Sprite s = image.sprite;
        UITheme.SpriteToken token = shape.Resolve(theme);
        float source = token != null && token.sprite == s ? token.SourceCornerRadius : s != null ? s.border.x : 0f;
        if (cornerRadius <= 0f || s == null || source <= 0f || image.pixelsPerUnit <= 0f)
            return pixelsPerUnitMultiplier;
        return source / (image.pixelsPerUnit * cornerRadius);
    }
}

/// <summary>
/// A reusable panel / card / window / background surface: the shared Look, plus the padding and spacing of a layout
/// group on the panel. Put a <see cref="StyledSurface"/> on an Image to apply it. A panel has no label, state tints or
/// size of its own, so a new panel style leaves those alone.
/// </summary>
[CreateAssetMenu(fileName = "PanelStyle", menuName = DesignSystemNames.AssetMenuRoot + "Panel Style")]
public class UISurfaceStyle : UIComponentStyle
{
    [Tooltip("Write padding and spacing onto a LayoutGroup on the same object.")]
    [SerializeField] private bool applyLayout = false;
    // RectOffset's setters may not run inside a ScriptableObject constructor, so the parameterless form is the
    // only safe initializer; the authored default is written in Reset.
    [SerializeField] private RectOffset padding = new RectOffset();
    [SerializeField] private float spacing = 8f;

    public SurfaceLook Look => Background;

    private void Reset() => padding = new RectOffset(16, 16, 16, 16);

    protected override void OnFirstEnable()
    {
        HeightSource = ButtonHeightSource.KeepAuthored;
        LabelFontSource = LabelFontSource.KeepAuthored;
        ApplyStateTints = false;
    }

    public bool ApplyTo(Image image) => Background.ApplyTo(image, Theme);

    public bool ApplyLayout(LayoutGroup group)
    {
        if (!applyLayout || group == null) return false;
        bool changed = false;

        RectOffset p = group.padding;
        if (p.left != padding.left || p.right != padding.right || p.top != padding.top || p.bottom != padding.bottom)
        {
            group.padding = new RectOffset(padding.left, padding.right, padding.top, padding.bottom);
            changed = true;
        }

        if (group is HorizontalOrVerticalLayoutGroup line && !Mathf.Approximately(line.spacing, spacing))
        {
            line.spacing = spacing;
            changed = true;
        }
        else if (group is GridLayoutGroup grid && grid.spacing != new Vector2(spacing, spacing))
        {
            grid.spacing = new Vector2(spacing, spacing);
            changed = true;
        }
        return changed;
    }
}
