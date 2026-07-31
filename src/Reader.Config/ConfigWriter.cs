namespace OpenReader.Config;

/// <summary>
/// Persists a single layer's <see cref="ReaderConfig"/> to disk as JSON.
/// Used by the settings UI to save user-edited config back to the user
/// layer; <see cref="ConfigStore"/>'s file watcher then triggers reload.
/// </summary>
public static class ConfigWriter
{
    public static void WriteToFile(string path, ReaderConfig config)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(config);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = ConfigSerializer.Serialize(config);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        // Atomic replace so a partial write never leaves a half-parsed file
        // for the watcher to choke on.
        if (File.Exists(path))
        {
            File.Replace(temp, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temp, path);
        }
    }
}
