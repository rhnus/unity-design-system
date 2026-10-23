using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the looks already in the project into styles: near-identical looks are clustered, and each cluster big
/// enough becomes a style of its kind that reproduces its most common member. Every member then matches
/// its style with <see cref="MatchConfidence.Safe"/>, so migrating is lossless apart from normalising
/// near-duplicates; unifying further is a matter of editing a handful of styles afterwards.
/// </summary>
public static class StyleGenerator
{
    public static string Folder => DesignSystemAssets.DefaultFolder + "/Generated";

    public class Cluster
    {
        /// <summary>The cluster's most common look, which the generated style reproduces.</summary>
        public LookGroup Seed;
        public readonly List<LookGroup> Members = new List<LookGroup>();
        public string Name;
        public int Count => Members.Sum(m => m.Count);
    }

    /// <summary>
    /// Greedy clustering, biggest look first: a look joins the first cluster whose seed is the same kind of control
    /// with the same traits, sprite and transition kind, and colours within tolerance. Clusters below minCount are dropped and stay for review.
    /// </summary>
    public static List<Cluster> BuildClusters(IEnumerable<LookGroup> groups, float tolerance, int minCount)
    {
        var clusters = new List<Cluster>();
        foreach (LookGroup g in groups.Where(g => g.HasBackground && !g.IsPicture).OrderByDescending(g => g.Count))
        {
            Cluster home = clusters.FirstOrDefault(c => SameLook(c.Seed, g, tolerance));
            if (home == null) clusters.Add(home = new Cluster { Seed = g });
            home.Members.Add(g);
        }
        List<Cluster> kept = clusters.Where(c => c.Count >= minCount).OrderByDescending(c => c.Count).ToList();
        Name(kept);
        return kept;
    }

    private static bool SameLook(LookGroup a, LookGroup b, float tolerance) =>
        a.Kind == b.Kind && a.Traits.Key == b.Traits.Key && a.Sprite == b.Sprite && a.ImageType == b.ImageType && a.Transition == b.Transition
        && (a.Transition != UnityEngine.UI.Selectable.Transition.ColorTint || ButtonScanner.SameStates(a.Colors, b.Colors, tolerance))
        && ButtonScanner.ColorDistance(a.Visible, b.Visible) <= tolerance
        && (!a.HasLabel || !b.HasLabel || ButtonScanner.ColorDistance(a.Label, b.Label) <= tolerance);

    /// <summary>
    /// Names by what the controls are and look like — kind, colour family, then shape — so a list of generated styles
    /// reads as a description (Button_Orange_SemiRounded, Toggle_Checkbox_White_UISprite) rather than a ranking.
    /// Repeats get a number.
    /// </summary>
    private static void Name(List<Cluster> clusters)
    {
        var used = new Dictionary<string, int>();
        foreach (Cluster c in clusters)
        {
            string variety = c.Seed.Traits.Variety;
            string kind = string.IsNullOrEmpty(variety) ? c.Seed.Kind.DisplayName : $"{c.Seed.Kind.DisplayName}_{variety}";
            string name = $"{kind}_{StyleConsolidation.ColourFamily(c.Seed.Visible)}_{Tag(c.Seed.Sprite != null ? c.Seed.Sprite.name : null, "Flat")}";
            used[name] = used.TryGetValue(name, out int n) ? n + 1 : 1;
            c.Name = used[name] == 1 ? name : $"{name}_{used[name]}";
        }
    }

    private static string Tag(string text, string empty)
    {
        if (string.IsNullOrEmpty(text)) return empty;
        string tag = new string(System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text)
                                    .Where(char.IsLetterOrDigit).ToArray());
        if (tag.Length == 0) return "Sprite";
        return tag.Length > 20 ? tag.Substring(0, 20) : tag;
    }

    /// <summary>
    /// One style for a set whose Off and On members look different: the Off look as the style, the On look's fill,
    /// label colour and shape as its On state — so styling the set changes nothing a player sees.
    /// </summary>
    public static UIComponentStyle CreateForSet(ControlSet set, LookGroup off, LookGroup on, UITheme theme, bool recolorBaked = true)
    {
        var cluster = new Cluster { Seed = off, Name = $"{off.Kind.DisplayName}_{Tag(set.Name, "Group")}" };
        cluster.Members.Add(off);
        UIComponentStyle style = CreateStyles(new[] { cluster }, theme, recolorBaked)[0];
        if (!style.HasOnState) return style;
        SeedOnState(style, off, on, theme);
        EditorUtility.SetDirty(style);
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        return style;
    }

    /// <summary>Sets a style's On state to whatever the On look has that the Off look doesn't: fill, label colour, shape.</summary>
    public static void SeedOnState(UIComponentStyle style, LookGroup off, LookGroup on, UITheme theme)
    {
        var so = new SerializedObject(style);
        string prefix = $"{on.Kind.DisplayName}/{style.name}";
        bool recolor = style.Background.fillMode == SurfaceFillMode.Recolor;
        so.FindProperty("onFill.tokenId").stringValue = Token(theme, $"{prefix}/OnFill", recolor ? on.Visible : on.Fill);
        if (on.HasLabel && ButtonScanner.ColorDistance(on.Label, off.Label) > 0.004f)
            so.FindProperty("onLabelColor.tokenId").stringValue = Token(theme, $"{prefix}/OnLabel", on.Label);
        if (on.Sprite != null && on.Sprite != off.Sprite)
            so.FindProperty("onShape.tokenId").stringValue = theme.GetOrAddSprite($"Shape/{on.Sprite.name}", on.Sprite).id;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Creates one style asset per cluster in <see cref="Folder"/>, with sprite, fill and label tokens in the theme.
    /// With recolorBaked on, a sprite with colour baked in gets the Recolor fill mode and its visible colour as the
    /// fill token, so that colour is themable without new art; otherwise the fill reproduces the Image colour.
    /// </summary>
    public static List<UIComponentStyle> CreateStyles(IEnumerable<Cluster> clusters, UITheme theme, bool recolorBaked = true)
    {
        Directory.CreateDirectory(Folder);
        var created = new List<UIComponentStyle>();
        Undo.RecordObject(theme, "Generate styles");
        foreach (Cluster c in clusters)
        {
            LookGroup seed = c.Seed;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{c.Name}.asset");
            string name = Path.GetFileNameWithoutExtension(path);

            string tokenPrefix = $"{seed.Kind.DisplayName}/{name}";
            var style = (UIComponentStyle)ScriptableObject.CreateInstance(seed.Kind.StyleType);
            AssetDatabase.CreateAsset(style, path);
            var so = new SerializedObject(style);
            so.FindProperty("theme").objectReferenceValue = theme;

            SerializedProperty look = so.FindProperty("background");
            look.FindPropertyRelative(nameof(SurfaceLook.sprite)).objectReferenceValue = seed.Sprite;
            bool recolor = false;
            if (seed.Sprite != null)
            {
                UITheme.SpriteToken shape = theme.GetOrAddSprite($"Shape/{seed.Sprite.name}", seed.Sprite);
                look.FindPropertyRelative("shape.tokenId").stringValue = shape.id;
                recolor = recolorBaked && SurfaceLook.IsShape(seed.Sprite) && RecolorMaterials.HasBakedColour(seed.Sprite);
            }
            look.FindPropertyRelative(nameof(SurfaceLook.fillMode)).enumValueIndex =
                (int)(recolor ? SurfaceFillMode.Recolor : SurfaceFillMode.Tint);
            look.FindPropertyRelative(nameof(SurfaceLook.imageType)).enumValueIndex = (int)seed.ImageType;
            float radius = MostCommonRadius(c);
            look.FindPropertyRelative(nameof(SurfaceLook.cornerRadius)).floatValue = radius;
            look.FindPropertyRelative(nameof(SurfaceLook.pixelsPerUnitMultiplier)).floatValue = seed.Multiplier;
            look.FindPropertyRelative("fill.tokenId").stringValue = Token(theme, $"{tokenPrefix}/Fill", recolor ? seed.Visible : seed.Fill);

            if (seed.HasLabel)
                so.FindProperty("labelColor.tokenId").stringValue = Token(theme, $"{tokenPrefix}/Label", seed.Label);
            so.FindProperty("labelFontSource").enumValueIndex = (int)LabelFontSource.Theme;
            SeedStates(so, seed);
            seed.Kind.Seed(so, seed.Traits, (part, colour) => Token(theme, $"{tokenPrefix}/{part}", colour));
            so.ApplyModifiedPropertiesWithoutUndo();
            created.Add(style);
        }
        EditorUtility.SetDirty(theme);
        RecolorMaterials.Ensure(theme);
        AssetDatabase.SaveAssets();
        return created;
    }

    /// <summary>
    /// State tints that reproduce the seed's own colours under Color Tint, and none for any other transition, so a
    /// generated style never changes how its controls react to hover, press or selection.
    /// </summary>
    public static void SeedStates(SerializedObject style, LookGroup seed)
    {
        bool tints = seed.Transition == UnityEngine.UI.Selectable.Transition.ColorTint;
        style.FindProperty("applyStateTints").boolValue = tints;
        if (!tints) return;
        style.FindProperty("highlightedTint").colorValue = seed.Colors.highlightedColor;
        style.FindProperty("pressedTint").colorValue = seed.Colors.pressedColor;
        style.FindProperty("selectedTint").colorValue = seed.Colors.selectedColor;
        style.FindProperty("disabledTint").colorValue = seed.Colors.disabledColor;
        style.FindProperty("fadeDuration").floatValue = seed.Colors.fadeDuration;
    }

    // Weighted by button count and rounded to half a pixel, so 26.67 and 26.6 count as one corner.
    private static float MostCommonRadius(Cluster c) =>
        c.Members.Where(m => m.Radius > 0f)
                 .GroupBy(m => Mathf.Round(m.Radius * 2f) / 2f)
                 .OrderByDescending(g => g.Sum(m => m.Count))
                 .Select(g => g.Key)
                 .FirstOrDefault();

    /// <summary>An existing token of practically this colour, else a new one under the given name.</summary>
    private static string Token(UITheme theme, string name, Color colour)
    {
        UITheme.ColorToken same = theme.Colors.FirstOrDefault(t => ButtonScanner.ColorDistance(t.value, colour) < 0.004f);
        return (same ?? theme.SetColor(name, colour)).id;
    }
}
