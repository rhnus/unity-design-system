/// <summary>The tools' menu location, from <see cref="DesignSystemBranding"/>.</summary>
public static class DesignSystemMenu
{
    public const string Root = DesignSystemNames.MenuRoot;
    public const string TypographyRoot = DesignSystemNames.TypographyMenuRoot;

    /// <summary>A menu item as people read it in instructions: "Tools ▸ Design System ▸ Write Design System Doc".</summary>
    public static string Path(string item, string root = Root) => (root + item).Replace("/", " ▸ ");
}
