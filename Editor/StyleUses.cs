using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>One place a style is used: a control in a prefab, or an object in a scene.</summary>
public class StyleUse
{
    public string assetPath;
    public bool inScene;
    /// <summary>The object's path from the prefab root ("Root/Panel/Send"), or its name in a scene.</summary>
    public string objectPath;
    /// <summary>The control inside the prefab asset; null in a scene, which is read as text and never opened.</summary>
    public Transform control;
}

/// <summary>
/// Where styles are used, found by their GUIDs in the project's prefab and scene files. Prefabs are loaded as assets
/// and read through SerializedObject, so each use is the exact control; scenes are read as text, so a scene with
/// unsaved changes is never opened or touched. Nothing is written.
/// </summary>
public static class StyleUses
{
    public static Dictionary<Object, List<StyleUse>> Find(IEnumerable<Object> styles)
    {
        var byGuid = new Dictionary<string, Object>();
        foreach (Object style in styles.Where(s => s != null))
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(style));
            if (!string.IsNullOrEmpty(guid)) byGuid[guid] = style;
        }
        Dictionary<Object, List<StyleUse>> uses = byGuid.Values.Distinct().ToDictionary(s => s, s => new List<StyleUse>());
        if (byGuid.Count == 0) return uses;

        var pattern = new Regex("guid: (" + string.Join("|", byGuid.Keys) + ")");
        string[] files = Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories)
                                  .Where(f => f.EndsWith(".prefab") || f.EndsWith(".unity")).ToArray();
        try
        {
            for (int i = 0; i < files.Length; i++)
            {
                if (i % 200 == 0) EditorUtility.DisplayProgressBar("Finding where styles are used", files[i], (float)i / files.Length);
                string path = files[i].Replace('\\', '/');
                string text = File.ReadAllText(path);
                foreach (string guid in pattern.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value).Distinct())
                    uses[byGuid[guid]].AddRange(UsesIn(path, text, guid, byGuid[guid]));
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        return uses;
    }

    private static IEnumerable<StyleUse> UsesIn(string path, string text, string guid, Object style)
    {
        if (path.EndsWith(".prefab") && AssetDatabase.LoadAssetAtPath<GameObject>(path) is GameObject prefab)
        {
            List<Transform> controls = InPrefab(prefab, style);
            if (controls.Count > 0)
                return controls.Select(t => new StyleUse { assetPath = path, objectPath = PathOf(prefab.transform, t), control = t });
        }
        return ObjectsReferencing(text, guid).Select(name => new StyleUse { assetPath = path, inScene = path.EndsWith(".unity"), objectPath = name });
    }

    /// <summary>The objects under root with a component whose serialized fields reference the style, in hierarchy order.</summary>
    public static List<Transform> InPrefab(GameObject root, Object style)
    {
        var found = new List<Transform>();
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null || found.Contains(component.transform)) continue;
            using var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == style)
                {
                    found.Add(component.transform);
                    break;
                }
        }
        return found;
    }

    public static string PathOf(Transform root, Transform t)
    {
        var names = new List<string>();
        for (Transform at = t; at != null; at = at == root ? null : at.parent) names.Add(at.name);
        names.Reverse();
        return string.Join("/", names);
    }

    /// <summary>
    /// The names of the GameObjects whose components reference the GUID in a prefab or scene file, read from its YAML.
    /// A reference held only as an override on a nested prefab instance has no GameObject of its own and says so.
    /// </summary>
    public static List<string> ObjectsReferencing(string yaml, string guid)
    {
        var names = new Dictionary<string, string>();
        var owners = new List<string>();
        foreach (string doc in Regex.Split(yaml, @"^--- !u!", RegexOptions.Multiline))
        {
            Match head = Regex.Match(doc, @"^(\d+) &(-?\d+)");
            if (!head.Success) continue;
            if (head.Groups[1].Value == "1")
            {
                Match name = Regex.Match(doc, @"^\s+m_Name: (.*)$", RegexOptions.Multiline);
                if (name.Success) names[head.Groups[2].Value] = name.Groups[1].Value.Trim();
            }
            else if (doc.Contains("guid: " + guid))
            {
                Match owner = Regex.Match(doc, @"m_GameObject: \{fileID: (-?\d+)\}");
                owners.Add(owner.Success ? owner.Groups[1].Value : null);
            }
        }
        return owners.Select(id => id == null ? "(an override on a prefab instance)" : names.TryGetValue(id, out string n) ? n : $"(object {id})")
                     .Distinct().ToList();
    }
}
