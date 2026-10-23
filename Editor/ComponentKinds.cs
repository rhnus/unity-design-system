using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One kind of element the design system can style — buttons, toggles, panels… Each kind says which components it
/// handles, which style asset it uses, which component carries that style, and what sets its elements apart during
/// setup. The tools ask the registry instead of naming a kind, so adding a kind means adding a kind, not editing
/// every tool.
/// </summary>
public interface IComponentKind
{
    string DisplayName { get; }
    /// <summary>The style asset type this kind uses, e.g. <see cref="UIButtonStyle"/>.</summary>
    Type StyleType { get; }
    /// <summary>The component that carries that style, e.g. <see cref="StyledButton"/>.</summary>
    Type StyledType { get; }
    /// <summary>The component setup scans for. Other components the kind handles are styled by hand.</summary>
    Type ControlType { get; }
    /// <summary>An element with a label and size; false for panels, which have only a look.</summary>
    bool IsControl { get; }
    /// <summary>Whether the style's hover / press / selected tints apply — only to Selectables.</summary>
    bool HasStateTints { get; }
    /// <summary>Whether this kind styles the given component.</summary>
    bool Handles(Component target);
    /// <summary>The Image that visibly draws the element.</summary>
    Image BackgroundOf(Component target);
    /// <summary>The text the style's label settings act on, or null.</summary>
    TMP_Text LabelOf(Component target);
    /// <summary>Whether the style is meant for elements built like these — a checkbox style never suits an option card.</summary>
    bool Suits(UIComponentStyle style, ScanTraits traits);

    /// <summary>What sets this element apart within its kind, read during a scan — e.g. a toggle's checkmark.</summary>
    ScanTraits Read(Component target);
    /// <summary>Whether the style leaves this kind's own parts looking as they do on elements with these traits.</summary>
    bool Keeps(UIComponentStyle style, ScanTraits traits, float tolerance);
    /// <summary>Fills this kind's own fields on a style generated from elements with these traits.</summary>
    void Seed(SerializedObject style, ScanTraits traits, Func<string, Color, string> colourToken);
    /// <summary>Fields of this kind's style shown on a Styles-tab row besides the shared ones: (property path, label).</summary>
    IEnumerable<(string Path, string Label)> QuickFields { get; }
    /// <summary>The group this element belongs to and should share one style with — a toggle's ToggleGroup — or null.</summary>
    Component SetOf(Component target);
    /// <summary>Whether the element is in its On state; always false for kinds without one.</summary>
    bool IsOn(Component target);
}

/// <summary>What a scan reads from an element beyond the shared look. Elements group together only when keys match.</summary>
public class ScanTraits
{
    public static readonly ScanTraits None = new ScanTraits();
    public virtual string Key => "";
    /// <summary>A word for names and lists, e.g. "Checkbox"; empty for none.</summary>
    public virtual string Variety => "";
}

/// <summary>The registered kinds, most specific first: the first kind that handles a component styles it.</summary>
public static class ComponentKinds
{
    public static readonly IComponentKind Buttons = new ButtonKind();
    public static readonly IComponentKind Toggles = new ToggleKind();
    public static readonly IComponentKind Panels = new PanelKind();
    public static readonly IComponentKind DragCards = new DragCardKind();

    public static readonly IReadOnlyList<IComponentKind> All = new[] { Toggles, Buttons, DragCards, Panels };

    public static IComponentKind For(Component target) => target == null ? null : All.FirstOrDefault(k => k.Handles(target));

    public static IComponentKind For(UIComponentStyle style) =>
        style == null ? null : All.FirstOrDefault(k => k.StyleType.IsInstanceOfType(style));

    /// <summary>Whether any kind can style this component.</summary>
    public static bool IsStyleable(Component target) => For(target) != null;

    /// <summary>Whether this style can be put on this component.</summary>
    public static bool Fits(UIComponentStyle style, Component target) => For(target)?.StyleType.IsInstanceOfType(style) == true;

    /// <summary>The elements setup scans under root: each kind's <see cref="IComponentKind.ControlType"/> that the kind handles.</summary>
    public static IEnumerable<Component> Scannable(GameObject root) =>
        All.SelectMany(k => root.GetComponentsInChildren(k.ControlType, true).Where(c => For(c) == k)).Distinct();

    /// <summary>Whether setup scans this component.</summary>
    public static bool IsScannable(Component target)
    {
        IComponentKind kind = For(target);
        return kind != null && kind.ControlType.IsInstanceOfType(target);
    }

    /// <summary>The styleable components under the given objects, each once — what the right-click style picker offers styles for.</summary>
    public static List<Component> Under(IEnumerable<GameObject> roots) =>
        roots.Where(r => r != null).SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
             .Where(IsStyleable).Cast<Component>().Distinct().ToList();
}

/// <summary>
/// Buttons — and, until a dedicated kind claims them, the other controls a button style suits: input fields and
/// dropdowns. Sliders and scrollbars are left out: their background is a track, not a shape.
/// </summary>
public sealed class ButtonKind : IComponentKind
{
    public string DisplayName => "Button";
    public Type StyleType => typeof(UIButtonStyle);
    public Type StyledType => typeof(StyledButton);
    public Type ControlType => typeof(Button);
    public bool IsControl => true;
    public bool HasStateTints => true;

    public bool Handles(Component target) =>
        target is Button || target is TMP_InputField || target is TMP_Dropdown || target is InputField || target is Dropdown;

    public Image BackgroundOf(Component target) => StyledSelectable.FindBackground(target as Selectable);
    public TMP_Text LabelOf(Component target) => StyledSelectable.FindLabel(target as Selectable);
    public bool Suits(UIComponentStyle style, ScanTraits traits) => true;
    public ScanTraits Read(Component target) => ScanTraits.None;
    public bool Keeps(UIComponentStyle style, ScanTraits traits, float tolerance) => true;
    public void Seed(SerializedObject style, ScanTraits traits, Func<string, Color, string> colourToken) { }
    public IEnumerable<(string Path, string Label)> QuickFields => Array.Empty<(string, string)>();
    public Component SetOf(Component target) => null;
    public bool IsOn(Component target) => false;
}

/// <summary>Toggles — checkboxes, radios, tabs, option cards — with their On/Off states and checkmark.</summary>
public sealed class ToggleKind : IComponentKind
{
    public string DisplayName => "Toggle";
    public Type StyleType => typeof(UIToggleStyle);
    public Type StyledType => typeof(StyledToggle);
    public Type ControlType => typeof(Toggle);
    public bool IsControl => true;
    public bool HasStateTints => true;

    public bool Handles(Component target) => target is Toggle;

    public Image BackgroundOf(Component target) => StyledSelectable.FindBackground((Selectable)target);
    public TMP_Text LabelOf(Component target) => StyledSelectable.FindLabel((Selectable)target);

    // A box beside a label (checkbox, radio) and a block that is the option itself (card, tab) are drawn differently.
    public bool Suits(UIComponentStyle style, ScanTraits traits) =>
        !(style is UIToggleStyle s) || !(traits is ToggleTraits t) || IsBox(s.ToggleType) == IsBox(t.Type);

    private static bool IsBox(ToggleType type) => type == ToggleType.Checkbox || type == ToggleType.Radio;

    public ScanTraits Read(Component target)
    {
        var toggle = (Toggle)target;
        Image box = BackgroundOf(target);
        bool row = box != null && box.transform != target.transform;
        Graphic mark = toggle.graphic;
        return new ToggleTraits
        {
            Type = !row ? ToggleType.Card : toggle.group != null ? ToggleType.Radio : ToggleType.Checkbox,
            HasMark = mark != null,
            MarkSprite = mark is Image image ? image.sprite : null,
            Mark = mark != null ? mark.color : Color.clear,
        };
    }

    // Sizes are left out on purpose, as for button height: unifying them is what styling is for.
    public bool Keeps(UIComponentStyle style, ScanTraits traits, float tolerance)
    {
        if (!(style is UIToggleStyle s) || !(traits is ToggleTraits t) || !t.HasMark) return true;
        bool sameSprite = s.CheckSprite == null || s.CheckSprite == t.MarkSprite;
        bool sameColour = !s.CheckColor.TryResolve(s.Theme, out Color tick) || ButtonScanner.ColorDistance(tick, t.Mark) <= tolerance;
        return sameSprite && sameColour;
    }

    // Toggle labels are body text, not button text, so a generated toggle style keeps each label's font.
    public void Seed(SerializedObject style, ScanTraits traits, Func<string, Color, string> colourToken)
    {
        style.FindProperty("labelFontSource").enumValueIndex = (int)LabelFontSource.KeepAuthored;
        if (!(traits is ToggleTraits t)) return;
        style.FindProperty("toggleType").enumValueIndex = (int)t.Type;
        if (!t.HasMark) return;
        style.FindProperty("checkSprite").objectReferenceValue = t.MarkSprite;
        style.FindProperty("checkColor.tokenId").stringValue = colourToken("Check", t.Mark);
    }

    public IEnumerable<(string Path, string Label)> QuickFields => new[] { ("onFill", "On fill") };
    public Component SetOf(Component target) => ((Toggle)target).group;
    public bool IsOn(Component target) => ((Toggle)target).isOn;
}

/// <summary>
/// Panels, cards, windows and backgrounds: a visible Image of a shape (a sliced sprite, or no sprite for a flat
/// colour) that isn't part of a control and is big enough to hold content. Pictures, progress fills, masks that
/// don't draw and thin dividers are not panels.
/// </summary>
public sealed class PanelKind : IComponentKind
{
    /// <summary>The smaller side, in px, below which an Image is a divider or a decoration rather than a panel.</summary>
    public const float MinSize = 40f;

    public string DisplayName => "Panel";
    public Type StyleType => typeof(UISurfaceStyle);
    public Type StyledType => typeof(StyledSurface);
    public Type ControlType => typeof(Image);
    public bool IsControl => false;
    public bool HasStateTints => false;

    public bool Handles(Component target)
    {
        if (!(target is Image image) || !image.enabled || image.color.a <= 0.01f) return false;
        bool shape = image.sprite == null ? image.type == Image.Type.Simple : image.type == Image.Type.Sliced && SurfaceLook.IsShape(image.sprite);
        if (!shape || image.GetComponentInParent<Selectable>(true) != null || DragCardKind.IsInsideCard(image)) return false;
        if (image.TryGetComponent(out Mask mask) && !mask.showMaskGraphic) return false;
        Rect r = image.rectTransform.rect;
        return Mathf.Min(r.width, r.height) >= MinSize;
    }

    public Image BackgroundOf(Component target) => target as Image;
    public TMP_Text LabelOf(Component target) => null;
    public bool Suits(UIComponentStyle style, ScanTraits traits) => true;
    public ScanTraits Read(Component target) => ScanTraits.None;
    public bool Keeps(UIComponentStyle style, ScanTraits traits, float tolerance) => true;

    public void Seed(SerializedObject style, ScanTraits traits, Func<string, Color, string> colourToken)
    {
        style.FindProperty("labelFontSource").enumValueIndex = (int)LabelFontSource.KeepAuthored;
        style.FindProperty("heightSource").enumValueIndex = (int)ButtonHeightSource.KeepAuthored;
    }

    public IEnumerable<(string Path, string Label)> QuickFields => Array.Empty<(string, string)>();
    public Component SetOf(Component target) => null;
    public bool IsOn(Component target) => false;
}

/// <summary>
/// Draggable cards — any component whose class derives from one named <see cref="BaseTypeName"/>, such as
/// CategorizeDragCard, WBSActivityDragItem and NetworkActivityDragItem. Matched by name because the design system
/// can't reference the game's own assembly. The card script keeps every behaviour; the style only paints its
/// handle, grip icon and body.
/// </summary>
public sealed class DragCardKind : IComponentKind
{
    /// <summary>Set per project in Design System Settings; empty turns drag cards off.</summary>
    public static string BaseTypeName => DesignSystemSettings.DragCardBaseType;

    public string DisplayName => "DragCard";
    public Type StyleType => typeof(UIDragCardStyle);
    public Type StyledType => typeof(StyledDragCard);
    public Type ControlType => typeof(MonoBehaviour);
    public bool IsControl => true;
    public bool HasStateTints => false;

    public bool Handles(Component target) => target is MonoBehaviour behaviour && IsCardType(behaviour.GetType());

    public static bool IsCardType(Type type) => IsCardType(type, BaseTypeName);

    public static bool IsCardType(Type type, string baseName)
    {
        if (string.IsNullOrEmpty(baseName)) return false;
        for (Type t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            if (t.Name == baseName) return true;
        return false;
    }

    /// <summary>Whether the component sits inside a card, as its handle, body or any other part.</summary>
    public static bool IsInsideCard(Component part)
    {
        for (Transform t = part.transform; t != null; t = t.parent)
            foreach (MonoBehaviour behaviour in t.GetComponents<MonoBehaviour>())
                if (behaviour != null && IsCardType(behaviour.GetType())) return true;
        return false;
    }

    public Image BackgroundOf(Component target) => StyledDragCard.FindParts(target.transform).Body;
    public TMP_Text LabelOf(Component target) => StyledDragCard.FindParts(target.transform).Title;
    public bool Suits(UIComponentStyle style, ScanTraits traits) => true;

    public ScanTraits Read(Component target)
    {
        StyledDragCard.DragCardParts parts = StyledDragCard.FindParts(target.transform);
        var traits = new DragCardTraits { Family = Family(target.GetType()) };
        if (parts.Handle != null)
        {
            traits.HasHandle = true;
            traits.HandleSprite = parts.Handle.sprite;
            traits.HandleType = parts.Handle.type;
            traits.HandleMultiplier = parts.Handle.pixelsPerUnitMultiplier;
            traits.Handle = parts.Handle.color;
        }
        if (parts.HandleInner != null) { traits.HasInner = true; traits.Inner = parts.HandleInner.color; }
        if (parts.Grip != null) { traits.HasGrip = true; traits.Grip = parts.Grip.color; traits.GripSprite = parts.Grip.sprite; }
        return traits;
    }

    // "CategorizeDragCard" → "Categorize", "WBSActivityDragItem" → "WBSActivity".
    private static string Family(Type type) => type.Name.Replace("DragCard", "").Replace("DragItem", "");

    public bool Keeps(UIComponentStyle style, ScanTraits traits, float tolerance)
    {
        if (!(style is UIDragCardStyle s) || !(traits is DragCardTraits t)) return true;
        UITheme theme = s.Theme;
        if (t.HasHandle && s.SetsHandle)
        {
            Sprite sprite = s.Handle.ResolveSprite(theme);
            if (sprite != null && sprite != t.HandleSprite) return false;
            if (s.Handle.fill.TryResolve(theme, out Color fill) && ButtonScanner.ColorDistance(fill, t.Handle) > tolerance) return false;
            if (s.Handle.imageType != t.HandleType) return false;
        }
        if (t.HasInner && s.HandleInnerFill.TryResolve(theme, out Color inner) && ButtonScanner.ColorDistance(inner, t.Inner) > tolerance) return false;
        if (t.HasGrip && s.GripColor.TryResolve(theme, out Color grip) && ButtonScanner.ColorDistance(grip, t.Grip) > tolerance) return false;
        if (t.HasGrip && s.GripSprite != null && s.GripSprite != t.GripSprite) return false;
        return true;
    }

    // Reproduces the handle exactly as the cards draw it, so adopting the style changes nothing on screen.
    public void Seed(SerializedObject style, ScanTraits traits, Func<string, Color, string> colourToken)
    {
        style.FindProperty("labelFontSource").enumValueIndex = (int)LabelFontSource.KeepAuthored;
        style.FindProperty("heightSource").enumValueIndex = (int)ButtonHeightSource.KeepAuthored;
        if (!(traits is DragCardTraits t)) return;
        if (t.HasHandle)
        {
            SerializedProperty handle = style.FindProperty("handle");
            handle.FindPropertyRelative(nameof(SurfaceLook.sprite)).objectReferenceValue = t.HandleSprite;
            handle.FindPropertyRelative(nameof(SurfaceLook.imageType)).enumValueIndex = (int)t.HandleType;
            handle.FindPropertyRelative(nameof(SurfaceLook.pixelsPerUnitMultiplier)).floatValue = t.HandleMultiplier;
            handle.FindPropertyRelative(nameof(SurfaceLook.cornerRadius)).floatValue = 0f;
            handle.FindPropertyRelative("fill.tokenId").stringValue = colourToken("Handle", t.Handle);
        }
        if (t.HasInner) style.FindProperty("handleInnerFill.tokenId").stringValue = colourToken("HandleInner", t.Inner);
        if (t.HasGrip)
        {
            style.FindProperty("gripColor.tokenId").stringValue = colourToken("Grip", t.Grip);
            style.FindProperty("gripSprite").objectReferenceValue = t.GripSprite;
        }
    }

    public IEnumerable<(string Path, string Label)> QuickFields => new[] { ("handle.fill", "Handle") };
    public Component SetOf(Component target) => null;
    public bool IsOn(Component target) => false;
}

/// <summary>A card's handle as the scan found it, and which card script it belongs to.</summary>
public sealed class DragCardTraits : ScanTraits
{
    public string Family = "";
    public bool HasHandle, HasInner, HasGrip;
    public Sprite HandleSprite, GripSprite;
    public Image.Type HandleType;
    public float HandleMultiplier = 1f;
    public Color Handle, Inner, Grip;

    public override string Key =>
        $"{Family}|{HasHandle}|{(HandleSprite != null ? HandleSprite.GetInstanceID() : 0)}|{HandleType}|{HandleMultiplier:0.##}|" +
        $"{ColorUtility.ToHtmlStringRGBA(Handle)}|{HasInner}|{ColorUtility.ToHtmlStringRGBA(Inner)}|{HasGrip}|{ColorUtility.ToHtmlStringRGBA(Grip)}|" +
        $"{(GripSprite != null ? GripSprite.GetInstanceID() : 0)}";
    public override string Variety => Family;
}

public sealed class ToggleTraits : ScanTraits
{
    public ToggleType Type;
    public bool HasMark;
    public Sprite MarkSprite;
    public Color Mark;

    public override string Key =>
        $"{Type}|{HasMark}|{(MarkSprite != null ? MarkSprite.GetInstanceID() : 0)}|{ColorUtility.ToHtmlStringRGBA(Mark)}";
    public override string Variety => Type.ToString();
}
