using UnityEditor;

/// <summary>Makes the project's theme the active one in the editor, so right / wrong previews and tests outside a scene match play.</summary>
[InitializeOnLoad]
internal static class ActiveThemeLoader
{
    static ActiveThemeLoader() => EditorApplication.delayCall += () =>
    {
        if (UITheme.Active == null) UITheme.Active = DesignSystemAssets.FindSingle<UITheme>();
    };
}
