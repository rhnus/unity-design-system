/// <summary>
/// The Design System's menu locations and the folder it starts a new project in. They belong to the package, not to
/// a project: a project's own names, folders and roles live in its Design System Settings and type scale assets.
/// </summary>
public static class DesignSystemNames
{
    public const string MenuRoot = "Tools/Design System/";
    public const string TypographyMenuRoot = MenuRoot + "Typography/";
    public const string AssetMenuRoot = "Design System/";
    public const string TypographyAssetMenu = AssetMenuRoot + "Typography Scale";

    /// <summary>Where a project's first settings, theme and styles are created; afterwards the tools use the settings' folder.</summary>
    public const string DefaultAssetFolder = "Assets/DesignSystem";
}
