using OpenReader.Diagnostics;
using Serilog;

namespace OpenReader.Config;

/// <summary>
/// Manages the "app-specific override" layer at the top of <see cref="ConfigStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// The layer is rebuilt when the foreground process changes. If
/// <c>%AppData%\OpenReader\apps\&lt;exe&gt;\config.json</c> exists for the
/// new exe, its content becomes the top layer; otherwise the layer is empty.
/// </para>
/// <para>
/// We do not file-watch app overrides (a typical user has dozens of apps).
/// Edits take effect when focus next moves into that app.
/// </para>
/// </remarks>
public sealed class AppLayerSwitcher : IDisposable
{
    private readonly ConfigStore _store;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private string? _currentExe;
    private bool _disposed;

    public AppLayerSwitcher(ConfigStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _log = LoggerFactory.ForComponent("Config.AppLayer");
        // Add an initial empty in-memory layer that we'll replace on every switch.
        _store.AddLayer("app", null);
    }

    /// <summary>Replace the top layer with one built from the override file for <paramref name="executableName"/>.</summary>
    public void SwitchTo(string? executableName)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            if (string.Equals(_currentExe, executableName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _currentExe = executableName;
        }

        var snapshot = LoadAppOverride(executableName);
        _store.ReplaceLayer("app", snapshot);
        _log.Debug("app layer switched to {Exe} ({Has} override)",
            executableName ?? "(none)", snapshot is null ? "no" : "with");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }

    private static ReaderConfig? LoadAppOverride(string? exe)
    {
        if (string.IsNullOrEmpty(exe))
        {
            return null;
        }
        var path = ConfigPaths.AppConfigPath(exe);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return ConfigSerializer.Deserialize(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }
}
