using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Finds button styles that treat a picture — an icon, a character, a checkbox — as a button shape, and puts those
/// buttons back the way they looked: Recolor is switched to Tint with the original Image colour, and the style
/// stops following the theme height so an icon is never stretched to button height.
/// </summary>
public static class DesignSystemRepair
{
    public static List<UIComponentStyle> FindPictureStyles() =>
        DesignSystemAssets.FindAll<UIComponentStyle>().Where(NeedsRepair).ToList();

    public static bool NeedsRepair(UIComponentStyle style)
    {
        Sprite sprite = style.Background.ResolveSprite(style.Theme);
        if (style.Locked || sprite == null || SurfaceLook.IsShape(sprite)) return false;
        return style.Background.fillMode == SurfaceFillMode.Recolor || style.HeightSource == ButtonHeightSource.Theme;
    }

    /// <summary>Repairs the styles and re-applies them everywhere. Returns how many styles were repaired.</summary>
    public static int Repair(IEnumerable<UIComponentStyle> styles)
    {
        UITheme theme = DesignSystemAssets.FindSingle<UITheme>();
        foreach (UIComponentStyle style in styles)
        {
            Undo.RecordObject(style, "Repair picture style");
            SurfaceLook look = style.Background;
            if (look.fillMode == SurfaceFillMode.Recolor)
            {
                look.fillMode = SurfaceFillMode.Tint;
                UITheme t = style.Theme != null ? style.Theme : theme;
                if (t != null && look.fill.TryResolve(t, out Color shown))
                {
                    Color original = OriginalFill(shown, ButtonScanner.SpriteCentreColour(look.ResolveSprite(t)));
                    Undo.RecordObject(t, "Repair picture style");
                    // A fresh token: the old one may be shared with another style, which must not change.
                    look.fill = ThemeColor.From(t.SetColor($"Button/{style.name}/Image", original));
                    EditorUtility.SetDirty(t);
                }
            }
            if (style.HeightSource == ButtonHeightSource.Theme) style.HeightSource = ButtonHeightSource.KeepAuthored;
            EditorUtility.SetDirty(style);
        }
        AssetDatabase.SaveAssets();
        DesignSystemAssets.ReapplyProject();
        return styles.Count();
    }

    /// <summary>
    /// The Image colour that, multiplied by the sprite as Tint does, shows what Recolor was told to show. Recolor's
    /// fill is the colour on screen (Image colour × sprite centre), so dividing the centre back out recovers it.
    /// </summary>
    public static Color OriginalFill(Color shown, Color centre)
    {
        float Channel(float s, float c) => c < 0.02f ? 1f : Mathf.Clamp01(s / c);
        return new Color(Channel(shown.r, centre.r), Channel(shown.g, centre.g), Channel(shown.b, centre.b), shown.a);
    }
}
