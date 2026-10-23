using UnityEngine;

/// <summary>
/// Marks a control whose look is deliberately its own — a one-off button drawn for a single screen — so the design
/// system never proposes a shared style for it: setup, Style Review and the scene overlay all leave it alone.
/// Remove the component to let the design system consider it again.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Design System/Unique Design")]
public class UniqueDesign : MonoBehaviour
{
    [Tooltip("Why this control doesn't follow a shared style, for the next person who wonders.")]
    [TextArea(2, 4)] public string reason;

    [Tooltip("Who decided, and when.")]
    public string decidedBy;
}
