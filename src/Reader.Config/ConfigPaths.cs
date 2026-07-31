namespace OpenReader.Config;

/// <summary>
/// Resolves on-disk locations of configuration files for each layer.
/// </summary>
/// <remarks>
/// The layout matches NVDA-style separation so users migrating from NVDA
/// recognise it: machine-wide under <c>%ProgramData%</c>, per-user under
/// <c>%AppData%</c>. App-specific overrides live alongside the user config in
/// an <c>apps/</c> subdirectory keyed by executable name.
/// </remarks>
public static class ConfigPaths
{
    public const string ConfigFileName = "config.json";

    public static string MachineDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OpenReader");

    public static string UserDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenReader");

    public static string MachineConfigPath => Path.Combine(MachineDirectory, ConfigFileName);

    public static string UserConfigPath => Path.Combine(UserDirectory, ConfigFileName);

    public static string ProfileDirectory(string profileName) =>
        Path.Combine(UserDirectory, "profiles", profileName);

    public static string ProfileConfigPath(string profileName) =>
        Path.Combine(ProfileDirectory(profileName), ConfigFileName);

    public static string AppConfigPath(string executableName) =>
        Path.Combine(UserDirectory, "apps", executableName, ConfigFileName);
}
