using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// How sharply a sliced Image draws its rounded corners. A sliced corner is drawn at
/// border / (pixelsPerUnit × multiplier) canvas pixels, so a multiplier that grows the corner beyond its source art
/// upscales it, and the edge goes soft and stepped. The fix is never a lower multiplier: it is a sprite whose corner
/// art is larger than the radius it draws, at a multiplier that keeps that radius.
/// </summary>
public static class CornerSharpness
{
    /// <summary>Above this, the corner is drawn larger than its source art and looks blurry at any resolution.</summary>
    public const float BlurryAbove = 1f;

    /// <summary>At or below this, the corner keeps its edge on a 2× screen and when the panel is scaled up.</summary>
    public const float CrispAtOrBelow = 0.5f;

    /// <summary>
    /// Canvas pixels drawn per source pixel of the corner, after Unity shrinks a corner to fit a small box. 0 for an
    /// Image that is not a sliced sprite with borders.
    /// </summary>
    public static float Upscale(Image image)
    {
        Sprite sprite = image != null ? image.sprite : null;
        if (sprite == null || image.type != Image.Type.Sliced || sprite.border == Vector4.zero) return 0f;
        float source = Mathf.Max(sprite.border.x, sprite.border.y);
        float radius = DrawnRadius(source, image.pixelsPerUnit, image.pixelsPerUnitMultiplier);
        Vector2 size = image.rectTransform.rect.size;
        float shortSide = Mathf.Min(size.x, size.y);
        if (shortSide > 0f) radius = Mathf.Min(radius, shortSide * 0.5f);
        return radius / source;
    }

    /// <summary>
    /// Canvas pixels drawn from <paramref name="sourcePixels"/> of the sprite, before any shrink-to-fit.
    /// Image.pixelsPerUnit is the sprite's pixels per unit over the canvas reference, 1 for a 100 PPU sprite. Pass the
    /// radius of the curve the art draws to get the visible corner radius: it is often narrower than the slice border
    /// (Unity's UISprite draws a 7.5px curve inside a 10px border).
    /// </summary>
    public static float DrawnRadius(float sourcePixels, float imagePixelsPerUnit, float multiplier) =>
        sourcePixels / (imagePixelsPerUnit * multiplier);

    /// <summary>The multiplier that draws a visible corner of <paramref name="radius"/> canvas pixels from a sprite
    /// whose art draws a curve of <paramref name="sourceCurve"/> pixels.</summary>
    public static float MultiplierFor(float radius, float sourceCurve, float imagePixelsPerUnit) =>
        sourceCurve / (imagePixelsPerUnit * radius);
}
