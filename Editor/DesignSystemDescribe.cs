using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plain-language answers to "what does this style actually give its buttons, and where does that come from?" —
/// one wording shared by the Styles board, the style Inspector and the type scale, so they never disagree.
/// </summary>
public static class DesignSystemDescribe
{
    /// <summary>E.g. "Button · Outfit-SemiBold · 20 px  (theme)", or "each label's own font".</summary>
    public static string LabelFont(UIComponentStyle style)
    {
        if (!style.TryGetLabelRoleId(out string role))
        {
            string own = style.LabelSize > 0f ? $"each label's own font · {style.LabelSize:0.#} px (this style)" : "each label's own font";
            return style.LabelFontSource == LabelFontSource.Theme && style.LabelSize <= 0f ? own + "  (theme text is off)" : own;
        }

        UITheme theme = style.Theme;
        TypographyScale scale = theme != null ? theme.Typography : null;
        TypographyScale.RoleStyle rs = scale != null ? scale.Get(role) : null;
        string size = style.LabelSize > 0f ? $"{style.LabelSize:0.#} px (this style)"
            : rs == null ? "" : rs.autoSize ? $"auto {rs.autoSizeMin:0.#}–{rs.autoSizeMax:0.#} px" : $"{rs.fontSize:0.#} px";
        string detail = rs == null && style.LabelSize <= 0f ? "" :
            $" · {(rs != null && rs.font != null ? rs.font.name : "label's own font")} · {size}";
        return $"{role}{detail}{(style.LabelFontSource == LabelFontSource.Theme ? "  (theme)" : "")}";
    }

    /// <summary>
    /// One-line summary for a style row, rich text: values taken from the theme in grey, values the style sets
    /// itself in bold — the way prefab overrides read — e.g. "Button 20 px · 56 px".
    /// </summary>
    public static string Short(UIComponentStyle style)
    {
        string font;
        if (style.TryGetLabelRoleId(out string role))
        {
            UITheme theme = style.Theme;
            TypographyScale.RoleStyle rs = theme != null && theme.Typography != null ? theme.Typography.Get(role) : null;
            font = style.LabelSize > 0f ? $"{role} {style.LabelSize:0.#} px"
                 : rs == null ? role.ToString() : rs.autoSize ? $"{role} auto" : $"{role} {rs.fontSize:0.#} px";
        }
        else font = style.LabelSize > 0f ? $"{style.LabelSize:0.#} px" : "own font";
        string height = style.Height > 0f ? $"{style.Height:0.#} px" + (style.Square ? " □" : "") : "own height";
        return $"{Mark(font, style.LabelFontSource == LabelFontSource.Theme && style.LabelSize <= 0f)} · " +
               $"{Mark(height, style.HeightSource == ButtonHeightSource.Theme && !style.Square)}";
    }

    private static string Mark(string text, bool fromTheme) => fromTheme ? $"<color=#8C8C8C>{text}</color>" : $"<b>{text}</b>";

    /// <summary>E.g. "56 px  (theme)", "100 px", or "each button's own height".</summary>
    public static string Height(UIComponentStyle style) =>
        FixedHeight(style) + (style.GrowWithContent && style.Height > 0f ? ", growing with its text" : "");

    private static string FixedHeight(UIComponentStyle style) => style.HeightSource switch
    {
        ButtonHeightSource.Theme => style.Height > 0f ? $"{style.Height:0.#} px  (theme)" : "each button's own height  (theme height not set)",
        ButtonHeightSource.Custom => $"{style.Height:0.#} px",
        ButtonHeightSource.NamedSize => style.NamedSize != null ? $"{style.NamedSize.name} · {style.Height:0.#} px  (theme)" : "⚠ size missing from the theme",
        _ => "each button's own height",
    };
}

/// <summary>
/// What was changed since the last visual check, so the Design System window can say "you changed N things —
/// check now" instead of relying on someone remembering. Lives for the editor session.
/// </summary>
public static class DesignChangeLog
{
    private static readonly List<string> entries = new List<string>();

    public static IReadOnlyList<string> Entries => entries;

    public static void Note(string what)
    {
        if (!entries.Contains(what)) entries.Add(what);
        QuickCheck.Schedule();
    }

    public static void Clear() => entries.Clear();
}
