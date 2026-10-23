using UnityEditor;
using UnityEngine;

/// <summary>Draws a <see cref="ThemeSize"/> as a dropdown of the theme's named sizes, each with its height.</summary>
[CustomPropertyDrawer(typeof(ThemeSize))]
public class ThemeSizeDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty idProp = property.FindPropertyRelative(nameof(ThemeSize.tokenId));
        label = EditorGUI.BeginProperty(position, label, property);
        Rect field = EditorGUI.PrefixLabel(position, label);

        SerializedProperty themeProp = property.serializedObject.FindProperty("theme");
        UITheme theme = themeProp != null && themeProp.objectReferenceValue is UITheme own ? own : ThemeColorDrawer.ProjectTheme;
        if (theme == null || theme.Sizes.Count == 0)
        {
            EditorGUI.LabelField(field, "No named sizes in the theme yet");
            EditorGUI.EndProperty();
            return;
        }

        var sizes = theme.Sizes;
        var names = new string[sizes.Count + 1];
        names[0] = "None";
        int current = 0;
        for (int i = 0; i < sizes.Count; i++)
        {
            names[i + 1] = $"{sizes[i].name} ({sizes[i].height:0.#})";
            if (sizes[i].id == idProp.stringValue) current = i + 1;
        }

        EditorGUI.showMixedValue = idProp.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        int picked = EditorGUI.Popup(field, current, names);
        if (EditorGUI.EndChangeCheck()) idProp.stringValue = picked == 0 ? string.Empty : sizes[picked - 1].id;
        EditorGUI.showMixedValue = false;
        EditorGUI.EndProperty();
    }
}
