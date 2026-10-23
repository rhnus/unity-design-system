using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a panel / card / window Image — and the padding of a LayoutGroup beside it — from a
/// <see cref="UISurfaceStyle"/>. Applies on the same schedule as every <see cref="StyledElement"/>. A panel's texts
/// belong to their own controls and the type scale, so it has no label.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public class StyledSurface : StyledElement
{
    [SerializeField] private UISurfaceStyle style;

    public UISurfaceStyle Style { get => style; set { style = value; Apply(); } }
    public override UIComponentStyle ComponentStyle => style;

    public override void AssignStyle(UIComponentStyle value) =>
        Style = value == null || value is UISurfaceStyle ? (UISurfaceStyle)value
              : throw new System.ArgumentException($"{value.name} is not a panel style.", nameof(value));

    // Added before panels shared the element pipeline, a surface has no background slot saved yet.
    protected override void Awake()
    {
        if (Background == null) AutoDetect();
        base.Awake();
    }

    protected override void OnEnable()
    {
        if (Background == null) AutoDetect();
        base.OnEnable();
    }

    protected override Image DetectBackground() => GetComponent<Image>();
    protected override TMP_Text DetectLabel() => null;
    protected override Graphic DetectIcon() => null;

    protected override bool ApplyExtras(UIComponentStyle s) => TryGetComponent(out LayoutGroup group) && style.ApplyLayout(group);
}
