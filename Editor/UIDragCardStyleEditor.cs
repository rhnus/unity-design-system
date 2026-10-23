using UnityEditor;

/// <summary>
/// The drag card style Inspector: the shared Look is the card's body, and the Handle section holds the fixed core —
/// the handle's frame, its inner panel on a two-tone handle, the grip icon's colour and the handle's width. Hover
/// tints are hidden: a card is not a Selectable, and its drag script owns how it reacts.
/// </summary>
[CustomEditor(typeof(UIDragCardStyle))]
public class UIDragCardStyleEditor : ComponentStyleEditor
{
    protected override bool ShowsWidth => false;
    protected override bool ShowsIcon => false;

    protected override void DrawKindSections(UIComponentStyle style)
    {
        if (!StyleInspectorParts.Section("Handle", "The orange handle with the grip icon. Empty = keep each card's own.", true)) return;
        SerializedProperty handle = serializedObject.FindProperty("handle");
        StyleInspectorParts.Field(handle.FindPropertyRelative("fill"), "Fill");
        StyleInspectorParts.Field(handle.FindPropertyRelative("sprite"), "Shape sprite");
        StyleInspectorParts.Field(handle.FindPropertyRelative("cornerRadius"), "Corner (px)", "0 = use the PPU multiplier as-is.");
        StyleInspectorParts.Field(serializedObject.FindProperty("handleInnerFill"), "Inner fill", "Only on cards with a two-tone handle.");
        StyleInspectorParts.Field(serializedObject.FindProperty("gripColor"), "Grip icon colour");
        StyleInspectorParts.Field(serializedObject.FindProperty("gripSprite"), "Grip icon", "Empty = keep each card's own.");
        StyleInspectorParts.Field(serializedObject.FindProperty("handleWidth"), "Width (px)", "0 = keep each card's own.");
    }
}
