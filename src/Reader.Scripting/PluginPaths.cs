namespace Aura.Scripting;

/// <summary>
/// Resolves on-disk locations of plugins. Plugins live under
/// <c>%AppData%\Aura\plugins\</c>; each plugin is its own subdirectory
/// containing a <c>manifest.json</c>.
/// </summary>
public static class PluginPaths
{
    /// <summary>The default user-scope plugins root.</summary>
    public static string UserPluginsRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AURA",
            "plugins");

    /// <summary>
    /// First-party app modules shipped alongside the host executable.
    /// Resolved relative to <see cref="AppContext.BaseDirectory"/>; on a
    /// dev build that's <c>bin/Debug/.../app-modules</c>, on an installed
    /// build it's <c>%ProgramFiles%\Aura\app-modules</c>.
    /// </summary>
    public static string ShippedAppModulesRoot =>
        Path.Combine(AppContext.BaseDirectory, "app-modules");
}
