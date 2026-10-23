using System;
using UnityEngine;

/// <summary>
/// The selected / right / wrong colours an answer option takes, as <see cref="UITheme"/> tokens, so every component
/// that holds one paints from the same Feedback tokens. An unset token leaves the caller's own colour.
/// Unlike <see cref="AnswerFeedback"/>, which restyles every graded answer once the theme switches it on, a palette
/// only affects the component that holds it.
/// </summary>
[Serializable]
public class AnswerPalette
{
    [Tooltip("Theme the tokens come from. Empty = the active theme.")]
    public UITheme theme;
    public ThemeColor selectedFill;
    public ThemeColor selectedBorder;
    public ThemeColor rightFill;
    public ThemeColor rightBorder;
    public ThemeColor wrongFill;
    public ThemeColor wrongBorder;

    /// <summary>The token's colour, or <paramref name="fallback"/> when the token is unset or missing.</summary>
    public Color Pick(ThemeColor token, Color fallback)
    {
        UITheme source = theme != null ? theme : UITheme.Active;
        return token.TryResolve(source, out Color color) ? color : fallback;
    }
}
