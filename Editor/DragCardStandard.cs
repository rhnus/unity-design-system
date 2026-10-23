using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One drag card style is the standard; every other card family converges on it in steps rather than at once.
/// Following the standard makes a family's style a variant of it that takes only the <see cref="CoreFields"/> — the
/// handle — and keeps everything else its own, so a family still shows its own body and
/// information. Any other field can later be set to follow too, from the style's Inspector, one at a time.
/// </summary>
public static class DragCardStandard
{
    /// <summary>What every card shares once it follows the standard: the handle. Right / wrong comes from the theme.</summary>
    public static readonly string[] CoreFields = { "handle", "handleInnerFill", "gripColor", "gripSprite", "handleWidth" };

    public static UIDragCardStyle Find()
    {
        DesignSystemSettings settings = DesignSystemSettings.Find();
        return settings != null ? settings.standardDragCard : null;
    }

    public static void Set(UIDragCardStyle style)
    {
        DesignSystemSettings settings = DesignSystemSettings.GetOrCreate();
        Undo.RecordObject(settings, "Set standard drag card");
        settings.standardDragCard = style;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        DesignChangeLog.Note($"{style.name} is the standard drag card");
    }

    /// <summary>Whether the style takes every core field from the standard.</summary>
    public static bool Follows(UIDragCardStyle style, UIDragCardStyle standard) =>
        style == standard || (style.BaseStyle == standard && CoreFields.All(f => !VariantSync.IsOverridden(style, f)));

    /// <summary>
    /// What following the standard would change on the style's cards, in words — empty when it would change
    /// nothing. Compares what each card shows (resolved colours, sprites, sizes), not the stored token ids, so two
    /// styles drawing the same colour through different tokens count as the same.
    /// </summary>
    public static List<string> Differences(UIDragCardStyle style, UIDragCardStyle standard)
    {
        var differences = new List<string>();
        UITheme a = style.Theme, b = standard.Theme;
        if (standard.SetsHandle)
        {
            Compare(differences, "handle colour", Colour(style.Handle.fill, a), Colour(standard.Handle.fill, b));
            Compare(differences, "handle shape", Name(style.Handle.ResolveSprite(a)), Name(standard.Handle.ResolveSprite(b)));
            Compare(differences, "handle corner", Corner(style.Handle), Corner(standard.Handle));
        }
        Compare(differences, "handle inner colour", Colour(style.HandleInnerFill, a), Colour(standard.HandleInnerFill, b), standard.HandleInnerFill.IsSet);
        Compare(differences, "grip icon colour", Colour(style.GripColor, a), Colour(standard.GripColor, b), standard.GripColor.IsSet);
        Compare(differences, "grip icon", Name(style.GripSprite), Name(standard.GripSprite), standard.GripSprite != null);
        Compare(differences, "handle width", Px(style.HandleWidth), Px(standard.HandleWidth), standard.HandleWidth > 0f);
        return differences;
    }

    private static void Compare(List<string> into, string what, string mine, string theirs, bool standardSetsIt = true)
    {
        if (standardSetsIt && mine != theirs) into.Add($"{what} {mine} → {theirs}");
    }

    private static string Colour(ThemeColor colour, UITheme theme) =>
        colour.TryResolve(theme, out Color c) ? "#" + ColorUtility.ToHtmlStringRGB(c) : "own";
    private static string Name(Sprite sprite) => sprite != null ? sprite.name : "own";
    private static string Corner(SurfaceLook look) => look.cornerRadius > 0f ? $"{look.cornerRadius:0.#}px" : $"PPU ×{look.pixelsPerUnitMultiplier:0.##}";
    private static string Px(float px) => px > 0f ? $"{px:0.#}px" : "own";

    /// <summary>
    /// Makes the style follow the standard for the core fields and keep every other field its own. A visible change
    /// wherever <see cref="Differences"/> lists one; nothing else on the style's cards moves.
    /// </summary>
    public static void Follow(UIDragCardStyle style, UIDragCardStyle standard)
    {
        if (style == null || standard == null || style == standard) return;
        Undo.RecordObject(style, "Follow the standard drag card");
        style.BaseStyle = standard;
        style.Overrides.Clear();
        SerializedProperty p = new SerializedObject(style).GetIterator();
        for (bool more = p.NextVisible(true); more; more = p.NextVisible(false))
            if (!VariantSync.IsBookkeeping(p.name) && !CoreFields.Contains(p.name))
                style.Overrides.Add(p.name);
        // Hidden fields are skipped by NextVisible and are bookkeeping already, except these two.
        foreach (string hidden in new[] { "applyLabelRole" })
            if (!style.Overrides.Contains(hidden)) style.Overrides.Add(hidden);
        EditorUtility.SetDirty(style);
        VariantSync.Sync(style);
        AssetDatabase.SaveAssetIfDirty(style);
        DesignChangeLog.Note($"{style.name} follows the standard drag card");
        DesignSystemAssets.ReapplyOpen();
    }

    // ---- Adding a handle to a card that has none ----

    /// <summary>Whether the standard defines enough to build a handle from: its colour and the grip icon.</summary>
    public static bool CanBuildHandle(UIDragCardStyle standard) =>
        standard != null && standard.Handle.fill.IsSet && standard.GripSprite != null;

    /// <summary>Whether the standard handle can be added: the card lays its children out in a row, so a new first child sits beside its content.</summary>
    public static bool CanAddHandle(StyledDragCard card) =>
        card != null && card.Handle == null && card.TryGetComponent(out HorizontalLayoutGroup _);

    /// <summary>
    /// Builds the standard handle — a "Grip" image with a "DraggableIcons" grip icon — as the card's first child,
    /// then re-applies the card's style. A visible layout change: the card's content moves right by the handle's
    /// width plus the row's spacing.
    /// </summary>
    public static void AddHandle(StyledDragCard card, UIDragCardStyle standard, bool undo)
    {
        if (!CanAddHandle(card) || !CanBuildHandle(standard)) return;
        UITheme theme = standard.Theme;

        var gripGo = new GameObject("Grip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        if (undo) Undo.RegisterCreatedObjectUndo(gripGo, "Add standard handle");
        gripGo.transform.SetParent(card.transform, false);
        gripGo.transform.SetAsFirstSibling();
        var frame = gripGo.GetComponent<Image>();
        frame.raycastTarget = true;
        standard.Handle.ApplyTo(frame, theme);
        if (frame.sprite == null) frame.sprite = standard.Background.ResolveSprite(standard.Theme);
        gripGo.GetComponent<LayoutElement>().preferredWidth = standard.HandleWidth > 0f ? standard.HandleWidth : 30f;

        var iconGo = new GameObject("DraggableIcons", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(gripGo.transform, false);
        var icon = iconGo.GetComponent<Image>();
        icon.sprite = standard.GripSprite;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        if (standard.GripColor.TryResolve(theme, out Color ink)) icon.color = ink;
        ((RectTransform)iconGo.transform).sizeDelta = new Vector2(17f, 22.5f);

        card.AutoDetect();
        card.Apply();
    }
}
