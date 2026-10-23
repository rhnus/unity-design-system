using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws a <see cref="TextRoleIdAttribute"/> string as a choice of the type scale's roles. An empty id shows the
/// built-in field beside it, so older assets show the role they actually use; picking a built-in role sets both.
/// </summary>
[CustomPropertyDrawer(typeof(TextRoleIdAttribute))]
public class TextRoleIdDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var attr = (TextRoleIdAttribute)attribute;
        SerializedProperty builtIn = Sibling(property, attr.BuiltInField);
        string current = Current(property, builtIn);

        label = EditorGUI.BeginProperty(position, label, property);
        EditorGUI.showMixedValue = property.hasMultipleDifferentValues || (builtIn != null && builtIn.hasMultipleDifferentValues);
        EditorGUI.BeginChangeCheck();
        string picked = TextRoleField.Popup(position, label, current, TypographyAssets.Find());
        if (EditorGUI.EndChangeCheck()) Set(property, builtIn, picked);
        EditorGUI.showMixedValue = false;
        EditorGUI.EndProperty();
    }

    private static SerializedProperty Sibling(SerializedProperty property, string name)
    {
        string path = property.propertyPath;
        int dot = path.LastIndexOf('.');
        return property.serializedObject.FindProperty(dot < 0 ? name : path.Substring(0, dot + 1) + name);
    }

    private static string Current(SerializedProperty id, SerializedProperty builtIn) =>
        !string.IsNullOrEmpty(id.stringValue) || builtIn == null ? id.stringValue : ((TextRole)builtIn.enumValueIndex).ToString();

    private static void Set(SerializedProperty id, SerializedProperty builtIn, string picked)
    {
        id.stringValue = picked;
        if (builtIn != null && TextRoles.TryGetBuiltIn(picked, out TextRole role)) builtIn.enumValueIndex = (int)role;
    }
}

/// <summary>A dropdown of the type scale's roles, built-in and the project's own.</summary>
public static class TextRoleField
{
    public static string Popup(Rect rect, GUIContent label, string current, TypographyScale scale)
    {
        string[] ids = Ids(scale, current);
        int index = System.Array.IndexOf(ids, current);
        int picked = EditorGUI.Popup(rect, label, Mathf.Max(0, index), ids.Select(i => new GUIContent(i)).ToArray());
        return ids.Length == 0 ? current : ids[picked];
    }

    public static string Popup(string current, TypographyScale scale, params GUILayoutOption[] options)
    {
        string[] ids = Ids(scale, current);
        int index = System.Array.IndexOf(ids, current);
        int picked = EditorGUILayout.Popup(Mathf.Max(0, index), ids, options);
        return ids.Length == 0 ? current : ids[picked];
    }

    // A role the scale no longer defines stays in the list, so the field still shows what it holds.
    private static string[] Ids(TypographyScale scale, string current)
    {
        var ids = scale != null
            ? scale.RoleIds.ToList()
            : System.Enum.GetNames(typeof(TextRole)).ToList();
        if (!string.IsNullOrEmpty(current) && !ids.Contains(current)) ids.Add(current);
        return ids.ToArray();
    }
}
