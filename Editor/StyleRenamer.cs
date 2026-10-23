using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renames a style to the role it plays — Button_Primary rather than Button_Orange_SemiRounded — so the list reads
/// as a design system. Renaming the asset keeps every reference: buttons point at the asset, not its name.
/// </summary>
public static class StyleRenamer
{
    public static readonly string[] Roles = { "Primary", "Secondary", "Tertiary", "Destructive", "Success", "Ghost", "Link", "Icon" };

    public static void Rename(UIComponentStyle style, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || style.name == name) return;
        string path = AssetDatabase.GetAssetPath(style);
        string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string unique = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset"));
        string error = AssetDatabase.RenameAsset(path, unique);
        if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"[DesignSystem] Couldn't rename {style.name}: {error}");
        else DesignChangeLog.Note($"renamed to {unique}");
    }

    public static void Prompt(UIComponentStyle style) => RenameWindow.Show(style);

    /// <summary>
    /// Generated control styles still carrying their descriptive name, e.g. Button_Orange_SemiRounded. Panel styles
    /// have no roles: their descriptive name (Panel_White_SemiRounded) already says what they are.
    /// </summary>
    public static List<UIComponentStyle> Unnamed(IEnumerable<UIComponentStyle> styles) =>
        styles.Where(s => s != null && IsUnnamed(s, AssetDatabase.GetAssetPath(s))).ToList();

    private static bool IsUnnamed(UIComponentStyle style, string path) =>
        !style.IsVariant && ComponentKinds.For(style)?.IsControl == true && path.StartsWith(StyleGenerator.Folder + "/")
        && !Roles.Any(r => style.name.EndsWith("_" + r)) && !System.Enum.GetNames(typeof(ToggleType)).Any(t => style.name == "Toggle_" + t);

    /// <summary>
    /// Role names for generated styles, most-used first: for buttons, the most-used filled style is Primary, the
    /// most-used light one Secondary and the next Tertiary; for toggles, the most-used style of each type is named
    /// by its type. Others keep their names. A name another style already has is never reused.
    /// </summary>
    public static List<(UIComponentStyle style, string name)> SuggestRoles(IList<StyleUsage> usages)
    {
        var taken = new HashSet<string>(usages.Select(u => u.Style.name));
        var result = new List<(UIComponentStyle, string)>();
        void Take(StyleUsage u, string name)
        {
            if (u == null || taken.Contains(name)) return;
            taken.Add(name);
            result.Add((u.Style, name));
        }

        var unnamed = new HashSet<UIComponentStyle>(usages.Where(u => IsUnnamed(u.Style, u.Path)).Select(u => u.Style));
        List<StyleUsage> buttons = usages.Where(u => unnamed.Contains(u.Style) && u.Style is UIButtonStyle).OrderByDescending(u => u.References).ToList();
        StyleUsage primary = buttons.FirstOrDefault(u => !IsLight(u));
        StyleUsage secondary = buttons.FirstOrDefault(u => IsLight(u));
        Take(primary, "Button_Primary");
        Take(secondary, "Button_Secondary");
        Take(buttons.FirstOrDefault(u => u != primary && u != secondary), "Button_Tertiary");

        foreach (IGrouping<ToggleType, StyleUsage> type in usages.Where(u => unnamed.Contains(u.Style) && u.Style is UIToggleStyle)
                                                                 .GroupBy(u => ((UIToggleStyle)u.Style).ToggleType))
            Take(type.OrderByDescending(u => u.References).First(), "Toggle_" + type.Key);
        return result;
    }

    private static bool IsLight(StyleUsage u) => u.Family == "White" || u.Family == "Clear" || u.Family == "Grey";

    /// <summary>Suggests role names for the generated styles and renames them on one confirmation. Looks don't change.</summary>
    public static void NameGeneratedByRole()
    {
        List<(UIComponentStyle style, string name)> renames = SuggestRoles(StyleConsolidation.MeasureUsage());
        if (renames.Count == 0)
        {
            EditorUtility.DisplayDialog("Name styles by role", "Every generated style already has a role name, or the role names are taken.", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Name styles by role",
                string.Join("\n", renames.Select(r => $"{r.style.name}  →  {r.name}")) +
                "\n\nOnly the names change — every button and toggle keeps its style. Rename others from their ⋯ menu.", "Rename", "Cancel"))
            return;
        foreach ((UIComponentStyle style, string name) in renames) Rename(style, name);
        AssetDatabase.SaveAssets();
    }

    private class RenameWindow : EditorWindow
    {
        private UIComponentStyle style;
        private string value;
        private bool focus = true;

        public static void Show(UIComponentStyle style)
        {
            var w = CreateInstance<RenameWindow>();
            w.style = style;
            w.value = style.name;
            w.titleContent = new GUIContent("Rename style");
            Rect anchor = focusedWindow != null ? focusedWindow.position : new Rect(200, 200, 400, 200);
            w.position = new Rect(anchor.x + 60f, anchor.y + 80f, 320f, 70f);
            w.ShowUtility();
        }

        private void OnGUI()
        {
            GUI.SetNextControlName("name");
            value = EditorGUILayout.TextField(value);
            if (focus) { EditorGUI.FocusTextInControl("name"); focus = false; }
            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rename") || enter)
                {
                    Rename(style, value.Trim());
                    Close();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Cancel")) Close();
            }
        }
    }
}
