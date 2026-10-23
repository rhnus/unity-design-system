using UnityEditor;

/// <summary>
/// The toggle style Inspector: the shared Look and Label are the Off state, and below them sits only what a toggle
/// of this type needs — "When on" for every type, the checkmark for checkboxes and radios. Width and icon colour,
/// which toggles rarely use, move to Rarely needed. A field that is set always shows, whatever the type.
/// </summary>
[CustomEditor(typeof(UIToggleStyle))]
public class UIToggleStyleEditor : ComponentStyleEditor
{
    protected override bool ShowsWidth => false;
    protected override bool ShowsIcon => false;

    protected override void DrawKindHeader(UIComponentStyle style) =>
        StyleInspectorParts.Field(serializedObject.FindProperty("toggleType"), "Toggle type",
            "Checkbox and radio: a box beside a label, with a checkmark. Tab and option card: the whole toggle is the block that changes.");

    protected override void DrawKindSections(UIComponentStyle style)
    {
        var toggle = (UIToggleStyle)style;
        bool box = toggle.ToggleType == ToggleType.Checkbox || toggle.ToggleType == ToggleType.Radio;

        if (StyleInspectorParts.Section("When on", "Only what changes while the toggle is on. Empty = same as off.", true))
        {
            StyleInspectorParts.Field(serializedObject.FindProperty("onFill"), "Fill");
            StyleInspectorParts.Field(serializedObject.FindProperty("onLabelColor"), "Text colour");
            if (box || toggle.OnShape.IsSet)
                StyleInspectorParts.Field(serializedObject.FindProperty("onShape"), "Shape", "E.g. a filled box for an outlined one.");
        }

        bool checkmarkSet = toggle.CheckSprite != null || toggle.CheckColor.IsSet || toggle.CheckSize > 0f || toggle.BoxSize > 0f;
        if ((box || checkmarkSet) && StyleInspectorParts.Section("Checkmark", "The tick or dot shown when on, and the box it sits in.", box))
        {
            StyleInspectorParts.Field(serializedObject.FindProperty("checkSprite"), "Sprite", "Empty = keep each toggle's own.");
            StyleInspectorParts.Field(serializedObject.FindProperty("checkColor"), "Colour", "Empty = keep each toggle's own.");
            StyleInspectorParts.Field(serializedObject.FindProperty("checkSize"), "Size (px)", "0 = keep each toggle's own.");
            StyleInspectorParts.Field(serializedObject.FindProperty("boxSize"), "Box size (px)", "0 = keep each toggle's own.");
        }
    }
}
