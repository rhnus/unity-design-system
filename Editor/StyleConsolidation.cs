using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>How much one button style is used, and where.</summary>
public class StyleUsage
{
    public UIComponentStyle Style;
    public string Guid;
    public string Path;
    public int References;
    public readonly List<string> Files = new List<string>();
    /// <summary>Assets other than prefabs, scenes and the settings that refer to this style; a style with any is never deleted.</summary>
    public readonly List<string> OtherReferences = new List<string>();
    public Color Visible;
    public string Family;

    public bool IsGenerated => Path.StartsWith(StyleGenerator.Folder + "/");
}

/// <summary>
/// Turns the many near-duplicate styles setup creates into a few roles. Styles are grouped by colour family — the
/// way a designer names them — and a whole group merges into one kept style in one step: every button using a
/// merged style switches to the kept one, in every prefab and scene, and the merged styles are removed.
/// </summary>
public static class StyleConsolidation
{
    public class MergeGroup
    {
        public string Family;
        public List<StyleUsage> Members;
        public StyleUsage Keep;
        public int References => Members.Sum(m => m.References);
    }

    /// <summary>Counts every reference to every style by reading prefab, scene and asset files as text.</summary>
    public static List<StyleUsage> MeasureUsage()
    {
        var byGuid = new Dictionary<string, StyleUsage>();
        foreach (UIComponentStyle style in DesignSystemAssets.FindAll<UIComponentStyle>())
        {
            string path = AssetDatabase.GetAssetPath(style);
            Color visible = VisibleColour(style);
            byGuid[AssetDatabase.AssetPathToGUID(path)] = new StyleUsage
            {
                Style = style, Guid = AssetDatabase.AssetPathToGUID(path), Path = path,
                Visible = visible, Family = ColourFamily(visible),
            };
        }
        if (byGuid.Count == 0) return new List<StyleUsage>();

        var pattern = new Regex("guid: (" + string.Join("|", byGuid.Keys) + ")");
        string settingsPath = AssetDatabase.GetAssetPath(DesignSystemSettings.Find());
        string[] files = Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".prefab") || f.EndsWith(".unity") || f.EndsWith(".asset")).ToArray();
        try
        {
            for (int i = 0; i < files.Length; i++)
            {
                if (i % 200 == 0) EditorUtility.DisplayProgressBar("Measuring style usage", files[i], (float)i / files.Length);
                string path = files[i].Replace('\\', '/');
                if (path == settingsPath || path.StartsWith(DesignSystemAssets.DefaultFolder + "/")) continue;
                foreach (Match m in pattern.Matches(File.ReadAllText(path)))
                {
                    StyleUsage u = byGuid[m.Groups[1].Value];
                    if (path.EndsWith(".asset")) { if (!u.OtherReferences.Contains(path)) u.OtherReferences.Add(path); continue; }
                    u.References++;
                    if (!u.Files.Contains(path)) u.Files.Add(path);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        return byGuid.Values.OrderByDescending(u => u.References).ToList();
    }

    /// <summary>What a style's controls look like at a glance: the fill under Recolor, fill × sprite under Tint.</summary>
    public static Color VisibleColour(UIComponentStyle style, bool on = false)
    {
        SurfaceLook look = style.LookFor(on);
        Sprite sprite = look.ResolveSprite(style.Theme);
        Color centre = ButtonScanner.SpriteCentreColour(sprite);
        if (!look.fill.TryResolve(style.Theme, out Color fill)) return centre;
        return look.fillMode == SurfaceFillMode.Recolor && look.shape.IsSet ? fill : fill * centre;
    }

    /// <summary>A plain colour name — the vocabulary a designer groups buttons by.</summary>
    public static string ColourFamily(Color c)
    {
        if (c.a < 0.05f) return "Clear";
        Color.RGBToHSV(c, out float h, out float s, out float v);
        if (v < 0.2f) return "Black";
        if (s < 0.12f) return v > 0.85f ? "White" : "Grey";
        float deg = h * 360f;
        if (deg < 15f || deg >= 345f) return "Red";
        if (deg < 45f) return "Orange";
        if (deg < 70f) return "Yellow";
        if (deg < 165f) return "Green";
        if (deg < 200f) return "Teal";
        if (deg < 255f) return "Blue";
        if (deg < 290f) return "Purple";
        return "Pink";
    }

    /// <summary>
    /// Families with more than one used style, each suggesting its most-used style as the one to keep. Styles
    /// nothing uses are left out — they are deleted, not merged.
    /// </summary>
    public static List<MergeGroup> SuggestGroups(IEnumerable<StyleUsage> usages) =>
        usages.Where(u => u.References > 0 && !u.Style.IsVariant)
              .GroupBy(u => (u.Family, Kind: u.Style.GetType()))
              .Where(g => g.Count() > 1)
              .Select(g => new MergeGroup
              {
                  Family = g.Key.Family,
                  Members = g.OrderByDescending(u => u.References).ToList(),
                  Keep = g.OrderByDescending(u => u.References).First(),
              })
              .OrderByDescending(g => g.References)
              .ToList();

    /// <summary>
    /// Points every StyledButton under root that uses one of the merged styles at the kept one, and restyles it.
    /// A button inside a nested prefab instance is only changed where this file overrides its style; otherwise its
    /// own prefab carries the reference and is merged in its own right. Returns how many buttons changed.
    /// </summary>
    public static int Repoint(GameObject root, ICollection<UIComponentStyle> merged, UIComponentStyle keep)
    {
        int changed = 0;
        foreach (StyledElement b in root.GetComponentsInChildren<StyledElement>(true))
        {
            var so = new SerializedObject(b);
            SerializedProperty prop = so.FindProperty("style");
            if (!(prop.objectReferenceValue is UIComponentStyle current) || !merged.Contains(current)) continue;
            bool instance = PrefabUtility.IsPartOfPrefabInstance(b);
            if (instance && !prop.prefabOverride) continue;

            prop.objectReferenceValue = keep;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (instance) b.Apply();
            else DesignSystemAssets.Attach(b.GetComponents<Component>().First(c => ComponentKinds.Fits(keep, c)), keep, recordUndo: false);
            changed++;
        }
        return changed;
    }

    /// <summary>
    /// Merges the given styles into keep across every prefab and scene that uses them, then removes the merged
    /// styles and the generated colour tokens only they used. Returns how many buttons changed.
    /// </summary>
    public static int Merge(IList<StyleUsage> merged, StyleUsage keep)
    {
        merged = merged.Where(m => m.Style != keep.Style && !m.Style.Locked && m.Style.GetType() == keep.Style.GetType()).ToList();
        var mergedStyles = new HashSet<UIComponentStyle>(merged.Select(m => m.Style));
        int changed = 0;

        List<string> files = merged.SelectMany(m => m.Files).Distinct().ToList();
        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                EditorUtility.DisplayProgressBar("Merging styles", path, (float)i / files.Count);
                if (path.EndsWith(".prefab"))
                {
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        int n = Repoint(root, mergedStyles, keep.Style);
                        if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                        changed += n;
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
                else
                {
                    ButtonScanner.WithScene(path, (scene, wasOpen) =>
                    {
                        int n = scene.GetRootGameObjects().Sum(r => Repoint(r, mergedStyles, keep.Style));
                        if (n > 0 && wasOpen) EditorSceneManager.MarkSceneDirty(scene);
                        changed += n;
                        return n > 0;
                    });
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        DesignSystemSettings settings = DesignSystemSettings.Find();
        if (settings != null && mergedStyles.Contains(settings.defaultButtonStyle) && keep.Style is UIButtonStyle keptButton)
        {
            settings.defaultButtonStyle = keptButton;
            EditorUtility.SetDirty(settings);
        }

        Delete(merged.Where(m => m.OtherReferences.Count == 0).ToList());
        foreach (StyleUsage m in merged.Where(m => m.OtherReferences.Count > 0))
            Debug.LogWarning($"[DesignSystem] Kept {m.Style.name}: still referenced by {string.Join(", ", m.OtherReferences)}.");
        DesignChangeLog.Note($"merged into {keep.Style.name}");
        Debug.Log($"[DesignSystem] Merged {merged.Count} style(s) into {keep.Style.name}; {changed} button(s) restyled.");
        return changed;
    }

    /// <summary>
    /// Retires a style: every button using it loses its StyledButton but keeps exactly how it looks now, then the
    /// style is deleted. A button inside a nested prefab instance only has its reference cleared where this file
    /// overrides it; its own prefab is visited separately. Returns how many buttons were unhooked.
    /// </summary>
    public static int Remove(StyleUsage usage)
    {
        UIComponentStyle style = usage.Style;
        if (style.Locked)
        {
            Debug.LogWarning($"[DesignSystem] {style.name} is locked — unlock it in its Inspector to remove it.");
            return 0;
        }
        int removed = 0;
        try
        {
            for (int i = 0; i < usage.Files.Count; i++)
            {
                string path = usage.Files[i];
                EditorUtility.DisplayProgressBar("Removing style", path, (float)i / usage.Files.Count);
                if (path.EndsWith(".prefab"))
                {
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        int n = Unhook(root, style);
                        if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                        removed += n;
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
                else
                {
                    ButtonScanner.WithScene(path, (scene, wasOpen) =>
                    {
                        int n = scene.GetRootGameObjects().Sum(r => Unhook(r, style));
                        if (n > 0 && wasOpen) EditorSceneManager.MarkSceneDirty(scene);
                        removed += n;
                        return n > 0;
                    });
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        DesignSystemSettings settings = DesignSystemSettings.Find();
        if (settings != null && settings.defaultButtonStyle == style)
        {
            settings.defaultButtonStyle = null;
            EditorUtility.SetDirty(settings);
        }
        if (usage.OtherReferences.Count == 0) Delete(new[] { usage });
        else Debug.LogWarning($"[DesignSystem] Kept {style.name}: still referenced by {string.Join(", ", usage.OtherReferences)}.");
        DesignChangeLog.Note($"removed {style.name}");
        return removed;
    }

    /// <summary>Takes the style off every button under root that uses it, leaving each button's look untouched.</summary>
    public static int Unhook(GameObject root, UIComponentStyle style)
    {
        int n = 0;
        foreach (StyledElement b in root.GetComponentsInChildren<StyledElement>(true))
        {
            if (b.ComponentStyle != style) continue;
            if (PrefabUtility.IsPartOfPrefabInstance(b))
            {
                var so = new SerializedObject(b);
                SerializedProperty prop = so.FindProperty("style");
                if (!prop.prefabOverride) continue;
                prop.objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Object.DestroyImmediate(b, true);
            }
            n++;
        }
        return n;
    }

    /// <summary>
    /// Deletes styles nothing refers to, plus the colour tokens setup generated for them that no remaining style
    /// uses. Hand-made tokens are never removed: a StyledColor somewhere may still point at one.
    /// </summary>
    public static void Delete(IList<StyleUsage> styles)
    {
        if (styles.Count == 0) return;
        UITheme theme = DesignSystemAssets.FindSingle<UITheme>();
        var candidates = new HashSet<string>();
        foreach (StyleUsage u in styles)
        {
            candidates.Add(u.Style.Background.fill.tokenId);
            candidates.Add(u.Style.LabelColor.tokenId);
            candidates.Add(u.Style.IconColor.tokenId);
        }

        var deleted = new HashSet<UIComponentStyle>(styles.Select(s => s.Style));
        foreach (StyleUsage u in styles) AssetDatabase.DeleteAsset(u.Path);

        if (theme != null)
        {
            var stillUsed = new HashSet<string>();
            foreach (UIComponentStyle s in DesignSystemAssets.FindAll<UIComponentStyle>().Where(s => !deleted.Contains(s)))
            {
                stillUsed.Add(s.Background.fill.tokenId);
                stillUsed.Add(s.LabelColor.tokenId);
                stillUsed.Add(s.IconColor.tokenId);
            }
            Undo.RecordObject(theme, "Remove unused tokens");
            foreach (string id in candidates.Where(id => !string.IsNullOrEmpty(id) && !stillUsed.Contains(id)))
                if (theme.GetToken(id)?.name?.StartsWith("Button/") == true) theme.RemoveColor(id);
            EditorUtility.SetDirty(theme);
        }
        AssetDatabase.SaveAssets();
    }
}
