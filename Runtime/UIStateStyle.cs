using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What one state of a stateful component paints: fill, border, text ink, an accent part, and opacity.</summary>
public struct StateLook
{
    public Color Fill;
    public Color Border;
    public Color Ink;
    /// <summary>A component's extra coloured part: a grip, a tape strip, a connector.</summary>
    public Color Accent;
    public float Opacity;

    public static StateLook Of(Color fill, Color border, Color ink, Color accent, float opacity = 1f) =>
        new StateLook { Fill = fill, Border = border, Ink = ink, Accent = accent, Opacity = opacity };
}

/// <summary>
/// The looks of a component that changes with game state rather than with pointer input — a name tag that is
/// Locked, Selected, Correct, Steer or Resolved — as <see cref="UITheme"/> tokens under the Figma variant's state
/// names. The component keeps its own state machine and asks the style for each state's look, so one asset
/// restyles every instance and a token edit restyles every style that uses it.
/// A state or field the style leaves empty keeps the caller's own colour.
/// </summary>
[CreateAssetMenu(fileName = "State_", menuName = DesignSystemNames.AssetMenuRoot + "State Style")]
public class UIStateStyle : ScriptableObject
{
    [Serializable]
    public class State
    {
        [Tooltip("The Figma variant's state name, e.g. Selected or Drop live.")]
        public string name;
        public ThemeColor fill;
        public ThemeColor border;
        public ThemeColor ink;
        [Tooltip("The component's extra coloured part, such as a grip or a tape strip.")]
        public ThemeColor accent;
        [Range(0f, 1f)] public float opacity = 1f;
    }

    [Tooltip("Theme the tokens come from. Empty = the active theme.")]
    [SerializeField] private UITheme theme;
    [SerializeField] private List<State> states = new List<State>();

    public UITheme Theme { get => theme; set => theme = value; }
    public IReadOnlyList<State> States => states;

    public bool Has(string stateName) => Find(stateName) != null;

    public State Find(string stateName) =>
        states.Find(s => s != null && string.Equals(s.name, stateName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The state's look, with every unset token, and every state the style lacks, left as <paramref name="fallback"/>.</summary>
    public StateLook Resolve(string stateName, StateLook fallback)
    {
        State state = Find(stateName);
        if (state == null) return fallback;
        UITheme source = theme != null ? theme : UITheme.Active;
        return new StateLook
        {
            Fill = state.fill.TryResolve(source, out Color fill) ? fill : fallback.Fill,
            Border = state.border.TryResolve(source, out Color border) ? border : fallback.Border,
            Ink = state.ink.TryResolve(source, out Color ink) ? ink : fallback.Ink,
            Accent = state.accent.TryResolve(source, out Color accent) ? accent : fallback.Accent,
            Opacity = state.opacity,
        };
    }

    /// <summary>Adds the state, or replaces the one already carrying this name.</summary>
    public State SetState(string stateName, ThemeColor fill = default, ThemeColor border = default, ThemeColor ink = default,
                          ThemeColor accent = default, float opacity = 1f)
    {
        State state = Find(stateName);
        if (state == null)
        {
            state = new State();
            states.Add(state);
        }
        state.name = stateName;
        state.fill = fill;
        state.border = border;
        state.ink = ink;
        state.accent = accent;
        state.opacity = opacity;
        return state;
    }
}
