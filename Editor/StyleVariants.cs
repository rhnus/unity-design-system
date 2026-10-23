using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Style variants, the way prefab variants work: a variant follows its base style except for the fields it
/// overrides. Following is done here in the editor by copying the base's values in, so a variant is an ordinary,
/// self-contained style at runtime.
/// </summary>
public static class VariantSync
{
    public enum Preset { IconAndText, IconOnly, Blank }

    // Identity and bookkeeping are the variant's own; everything else follows the base unless overridden.
    private static readonly HashSet<string> NeverCopied = new HashSet<string>
    {
        "m_Script", "m_Name", "m_ObjectHideFlags", "m_EditorClassIdentifier", "baseStyle", "overrides", "locked", "dataVersion",
    };

    /// <summary>Whether the field is the style's own bookkeeping, which never follows a base.</summary>
    public static bool IsBookkeeping(string field) => NeverCopied.Contains(field);

    // A text role is stored in two fields, the built-in role and its id (see TextRoles); overriding one keeps both.
    private static readonly Dictionary<string, string> Partners = new Dictionary<string, string>
    {
        { "labelRole", "labelRoleId" }, { "labelRoleId", "labelRole" },
    };

    private static bool Covers(string overridePath, string path) => path == overridePath || path.StartsWith(overridePath + ".");

    private static bool CoversWithPartner(string overridePath, string path) =>
        Covers(overridePath, path) || (Partners.TryGetValue(overridePath, out string partner) && Covers(partner, path));

    public static bool IsOverridden(UIComponentStyle style, string path) =>
        style != null && style.IsVariant && style.Overrides.Any(o => CoversWithPartner(o, path));

    public static void MarkOverridden(UIComponentStyle style, string path)
    {
        if (style == null || !style.IsVariant || IsOverridden(style, path)) return;
        style.Overrides.RemoveAll(o => o.StartsWith(path + "."));
        style.Overrides.Add(path);
        EditorUtility.SetDirty(style);
    }

    /// <summary>Drops an override so the field follows the base again.</summary>
    public static void Revert(UIComponentStyle style, string path)
    {
        Undo.RecordObject(style, "Revert to base");
        style.Overrides.RemoveAll(o => CoversWithPartner(path, o));
        EditorUtility.SetDirty(style);
        Sync(style);
        DesignSystemAssets.ReapplyOpen();
    }

    public static void RevertAll(UIComponentStyle style)
    {
        Undo.RecordObject(style, "Revert all to base");
        style.Overrides.Clear();
        EditorUtility.SetDirty(style);
        Sync(style);
        DesignSystemAssets.ReapplyOpen();
    }

    /// <summary>Copies the base's values into the variant, field by field, skipping overrides. Returns whether anything changed.</summary>
    public static bool Sync(UIComponentStyle variant) => Sync(variant, new HashSet<UIComponentStyle>());

    private static bool Sync(UIComponentStyle variant, HashSet<UIComponentStyle> visiting)
    {
        UIComponentStyle source = variant != null ? variant.BaseStyle : null;
        if (source == null || source == variant || !visiting.Add(variant)) return false;
        if (source.IsVariant) Sync(source, visiting);

        var from = new SerializedObject(source);
        var to = new SerializedObject(variant);
        bool changed = false;
        SerializedProperty p = from.GetIterator();
        bool enter = true;
        while (p.Next(enter))
        {
            string path = p.propertyPath;
            if (NeverCopied.Contains(path.Split('.')[0]) || IsOverridden(variant, path)) { enter = false; continue; }
            if (p.propertyType == SerializedPropertyType.Generic && p.hasChildren && !p.isArray) { enter = true; continue; }
            enter = false;
            SerializedProperty target = to.FindProperty(path);
            if (target == null || SerializedProperty.DataEquals(p, target)) continue;
            to.CopyFromSerializedProperty(p);
            changed = true;
        }
        if (changed) to.ApplyModifiedPropertiesWithoutUndo();
        return changed;
    }

    public static int SyncAll()
    {
        int n = 0;
        foreach (UIComponentStyle style in DesignSystemAssets.FindAll<UIComponentStyle>())
            if (style.IsVariant && Sync(style)) n++;
        return n;
    }

    public static string SuggestName(UIComponentStyle baseStyle, Preset preset) =>
        (baseStyle != null ? baseStyle.name : "Button") + preset switch
        {
            Preset.IconAndText => "_IconText",
            Preset.IconOnly => "_IconOnly",
            _ => "_Variant",
        };

    /// <summary>Creates a variant beside its base. The base and every button already using it are untouched.</summary>
    public static UIComponentStyle Create(UIComponentStyle baseStyle, string name, Preset preset, float labelSize = 12f)
    {
        string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(baseStyle)).Replace('\\', '/');
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset");
        UIComponentStyle variant = Object.Instantiate(baseStyle);
        variant.BaseStyle = baseStyle;
        variant.Overrides.Clear();
        variant.Locked = false;

        if (preset == Preset.IconAndText)
        {
            variant.LabelSize = labelSize;
            variant.Overrides.Add("labelSize");
        }
        else if (preset == Preset.IconOnly)
        {
            variant.Square = true;
            variant.Overrides.Add("square");
        }

        AssetDatabase.CreateAsset(variant, path);
        AssetDatabase.SaveAssets();
        DesignChangeLog.Note($"created {variant.name}");
        return variant;
    }
}

/// <summary>
/// "New Variant": pick a base and a preset, get a variant — applied only to the buttons it was opened for, if any.
/// Nothing that exists is changed.
/// </summary>
public class NewVariantWindow : EditorWindow
{
    private static readonly string[] PresetNames = { "Icon + text", "Icon only", "Blank" };

    private UIComponentStyle baseStyle;
    private VariantSync.Preset preset;
    private float labelSize = 12f;
    private string variantName;
    private bool nameEdited;
    private List<Component> targets;

    public static void Open(UIComponentStyle baseStyle, List<Component> targets = null)
    {
        var window = CreateInstance<NewVariantWindow>();
        window.baseStyle = baseStyle;
        window.targets = targets ?? new List<Component>();
        window.preset = VariantSync.Preset.IconAndText;
        window.variantName = VariantSync.SuggestName(baseStyle, window.preset);
        window.titleContent = new GUIContent("New style variant");
        Rect anchor = focusedWindow != null ? focusedWindow.position : new Rect(200, 200, 400, 300);
        window.position = new Rect(anchor.x + 40f, anchor.y + 60f, 340f, 180f);
        window.ShowUtility();
    }

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        baseStyle = (UIComponentStyle)EditorGUILayout.ObjectField(new GUIContent("Based on", "The variant follows this style except where it overrides."),
                                                               baseStyle, typeof(UIComponentStyle), false);
        preset = (VariantSync.Preset)GUILayout.Toolbar((int)preset, PresetNames);
        if (EditorGUI.EndChangeCheck() && !nameEdited) variantName = VariantSync.SuggestName(baseStyle, preset);

        switch (preset)
        {
            case VariantSync.Preset.IconAndText:
                labelSize = EditorGUILayout.FloatField(new GUIContent("Text size (px)", "Smaller text to make room for the icon."), labelSize);
                break;
            case VariantSync.Preset.IconOnly:
                EditorGUILayout.LabelField("Square: width matches height. Set its own height on the variant if needed.", EditorStyles.wordWrappedMiniLabel);
                break;
            default:
                EditorGUILayout.LabelField("Identical to the base until you change something on it.", EditorStyles.wordWrappedMiniLabel);
                break;
        }

        EditorGUI.BeginChangeCheck();
        variantName = EditorGUILayout.TextField("Name", variantName);
        if (EditorGUI.EndChangeCheck()) nameEdited = true;

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(baseStyle == null || string.IsNullOrWhiteSpace(variantName)))
        {
            string label = targets.Count > 0 ? $"Create and apply to {targets.Count} selected" : "Create";
            if (GUILayout.Button(label, GUILayout.Height(26)))
            {
                UIComponentStyle variant = VariantSync.Create(baseStyle, variantName.Trim(), preset, labelSize);
                if (targets.Count > 0) DesignSystemContextMenu.Apply(targets, variant);
                Selection.activeObject = variant;
                Close();
                GUIUtility.ExitGUI();
            }
        }
    }
}
