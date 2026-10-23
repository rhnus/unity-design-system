using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sprites the design system draws with that live in a third-party folder — a UI pack's demo sprites, say. Deleting
/// or updating that pack would break every style using them, so the check offers to move them into the design
/// system's own folder. Moving keeps the asset's GUID, so every Image, style and theme token still points at it.
/// </summary>
public static class ThirdPartySprites
{
    public static string Folder => DesignSystemAssets.DefaultFolder + "/Shapes";

    /// <summary>Texture paths, under a third-party folder, of the sprites the themes and styles use.</summary>
    public static List<string> Find(IEnumerable<UITheme> themes, IEnumerable<UIComponentStyle> styles, DesignSystemSettings settings)
    {
        IEnumerable<Sprite> sprites = themes.Where(t => t != null).SelectMany(t => t.Sprites.Select(s => s.sprite))
            .Concat(styles.Where(s => s != null).Select(s => s.Background.sprite));
        return sprites.Where(s => s != null).Select(AssetDatabase.GetAssetPath)
                      .Where(p => !string.IsNullOrEmpty(p) && settings.IsThirdParty(p))
                      .Distinct().OrderBy(p => p).ToList();
    }

    /// <summary>Moves the texture into the folder, keeping its GUID. Returns the new path, or null with a warning on failure.</summary>
    public static string Move(string path, string folder = null)
    {
        folder ??= Folder;
        EnsureFolder(folder);
        string target = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{Path.GetFileName(path)}");
        string error = AssetDatabase.MoveAsset(path, target);
        if (string.IsNullOrEmpty(error)) return target;
        Debug.LogWarning($"[DesignSystem] Couldn't move {path}: {error}");
        return null;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
