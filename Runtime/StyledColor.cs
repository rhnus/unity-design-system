using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds any Graphic's colour — an icon, a divider, a TMP text — to a <see cref="UITheme"/> token.
/// Applies on the same schedule as <see cref="StyledButton"/>.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
public class StyledColor : MonoBehaviour
{
    [Tooltip("Auto-filled in the editor when left empty.")]
    [SerializeField] private UITheme theme;
    [SerializeField] private ThemeColor color;

    public UITheme Theme { get => theme; set { theme = value; Apply(); } }
    public ThemeColor Color { get => color; set { color = value; Apply(); } }

    private void Awake()
    {
        if (Application.isPlaying) Apply();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) Apply();
    }

    /// <summary>Writes the token's colour onto the Graphic. Returns whether anything changed.</summary>
    public bool Apply()
    {
        var graphic = GetComponent<Graphic>();
        if (graphic == null || !color.TryResolve(theme, out UnityEngine.Color c) || graphic.color == c) return false;
        graphic.color = c;
        return true;
    }
}
