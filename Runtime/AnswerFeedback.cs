using UnityEngine;

/// <summary>How a graded answer looks: its fill, its border, and the mark (tick or cross) for a selection box.</summary>
public struct AnswerLook
{
    public Color Fill;
    public Color Border;
    /// <summary>The tick or cross, or null to leave each option's own mark.</summary>
    public Sprite Mark;
}

/// <summary>
/// One right / wrong look for every graded answer in the game — quiz options, option cards, drag cards — taken from
/// the active <see cref="UITheme"/>. A component asks once per verdict and paints the parts it already has, so no
/// prefab carries its own right / wrong fields. Until the theme switches it on, every component keeps its own colours.
/// </summary>
public static class AnswerFeedback
{
    /// <summary>The look for a right (<c>true</c>) or wrong answer, or false when the theme leaves it to each component.</summary>
    public static bool TryGet(bool correct, out AnswerLook look)
    {
        look = default;
        return UITheme.Active != null && UITheme.Active.TryGetAnswerLook(correct, out look);
    }
}
