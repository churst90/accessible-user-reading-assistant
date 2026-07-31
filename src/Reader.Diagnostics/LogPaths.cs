namespace OpenReader.Diagnostics;

/// <summary>Resolves the OpenReader log directory in a per-user, per-OS location.</summary>
public static class LogPaths
{
    public const string AppDirectoryName = "OpenReader";
    public const string LogsSubdirectory = "logs";

    /// <summary>The directory where the rotating log file lives. Created if missing.</summary>
    public static string LogDirectory
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(local, AppDirectoryName, LogsSubdirectory);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>The rolling log file path template (Serilog appends a date stamp).</summary>
    public static string LogFileTemplate => Path.Combine(LogDirectory, "openreader-.log");
}
