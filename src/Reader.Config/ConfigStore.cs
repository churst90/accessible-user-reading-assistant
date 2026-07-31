using OpenReader.Diagnostics;
using Serilog;

namespace OpenReader.Config;

/// <summary>
/// Owns the merged <see cref="ReaderConfig"/> and emits change events when
/// any source layer is reloaded.
/// </summary>
/// <remarks>
/// Source layers are added via <see cref="AddLayer"/> in the intended merge
/// order — defaults first, then machine, user, profile, app, script. Each
/// layer is either an in-memory snapshot or a file-backed source watched for
/// changes. Reload re-merges all layers and raises <see cref="Changed"/> if
/// the merged result differs.
/// </remarks>
public sealed class ConfigStore : IDisposable
{
    /// <summary>
    /// Debounce for file-watcher events. <c>FileSystemWatcher</c> fires
    /// multiple events for a single save (write, attribute, security,
    /// etc.) — we coalesce them into one reload by resetting a Timer
    /// rather than blocking a thread-pool worker with Thread.Sleep.
    /// </summary>
    private static readonly TimeSpan FileWatchDebounce = TimeSpan.FromMilliseconds(75);

    private readonly object _gate = new();
    private readonly List<Layer> _layers = new();
    private readonly ILogger _log;
    private readonly Timer _reloadTimer;
    private ReaderConfig _current = ReaderConfig.Defaults();
    private bool _disposed;

    public ConfigStore()
    {
        _log = LoggerFactory.ForComponent("Config.Store");
        _reloadTimer = new Timer(_ => Reload(), state: null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>The current merged config. Snapshot — safe to retain.</summary>
    public ReaderConfig Current
    {
        get { lock (_gate) { return _current; } }
    }

    /// <summary>Raised after a reload that changed the merged config. Subscribers run on the reloader's thread.</summary>
    public event Action<ReaderConfig>? Changed;

    /// <summary>Add a static in-memory layer. Useful for the defaults layer and for tests.</summary>
    public void AddLayer(string name, ReaderConfig? snapshot)
    {
        lock (_gate)
        {
            _layers.Add(new Layer(name, () => snapshot, Watcher: null));
        }
        Reload();
    }

    /// <summary>
    /// Replace the in-memory snapshot of a previously-added layer (matched by name).
    /// Used by the app-specific layer that swaps when foreground focus changes.
    /// No-op if the layer name is not registered.
    /// </summary>
    public void ReplaceLayer(string name, ReaderConfig? snapshot)
    {
        lock (_gate)
        {
            for (var i = 0; i < _layers.Count; i++)
            {
                if (string.Equals(_layers[i].Name, name, StringComparison.Ordinal))
                {
                    _layers[i] = _layers[i] with { Read = () => snapshot };
                    break;
                }
            }
        }
        Reload();
    }

    /// <summary>
    /// Add a file-backed layer. The file is read immediately and any future
    /// changes trigger reload. Missing files are tolerated as null layers.
    /// </summary>
    public void AddFileLayer(string name, string path)
    {
        var layer = BuildFileLayer(name, path);
        lock (_gate)
        {
            _layers.Add(layer);
        }
        Reload();
    }

    /// <summary>
    /// Insert a file-backed layer immediately before the named existing layer.
    /// If <paramref name="beforeLayerName"/> is not present the layer is
    /// appended (equivalent to <see cref="AddFileLayer"/>).
    /// </summary>
    /// <remarks>
    /// Used by the host to hot-swap the profile layer while preserving the
    /// merge order when the user changes their active profile at runtime —
    /// the profile must merge below the always-topmost app layer.
    /// </remarks>
    public void InsertFileLayer(string name, string path, string beforeLayerName)
    {
        ArgumentException.ThrowIfNullOrEmpty(beforeLayerName);
        var layer = BuildFileLayer(name, path);
        lock (_gate)
        {
            var index = -1;
            for (var i = 0; i < _layers.Count; i++)
            {
                if (string.Equals(_layers[i].Name, beforeLayerName, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                _layers.Add(layer);
            }
            else
            {
                _layers.Insert(index, layer);
            }
        }
        Reload();
    }

    /// <summary>
    /// Remove a previously-added layer by name. Disposes any associated
    /// <see cref="FileSystemWatcher"/>. No-op if the layer is not present.
    /// </summary>
    public bool RemoveLayer(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Layer? removed = null;
        lock (_gate)
        {
            for (var i = 0; i < _layers.Count; i++)
            {
                if (string.Equals(_layers[i].Name, name, StringComparison.Ordinal))
                {
                    removed = _layers[i];
                    _layers.RemoveAt(i);
                    break;
                }
            }
        }
        if (removed is null)
        {
            return false;
        }
        try { removed.Watcher?.Dispose(); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _log.Warning(ex, "watcher dispose threw for layer {Layer}", name);
        }
        Reload();
        return true;
    }

    /// <summary>True if a layer with the given name has been registered.</summary>
    public bool HasLayer(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }
        lock (_gate)
        {
            foreach (var layer in _layers)
            {
                if (string.Equals(layer.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    private Layer BuildFileLayer(string name, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = Path.GetDirectoryName(path);
        FileSystemWatcher? watcher = null;
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
                watcher = new FileSystemWatcher(directory, Path.GetFileName(path) ?? "*")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true,
                };
                watcher.Changed += (_, _) => OnFileChanged(path);
                watcher.Created += (_, _) => OnFileChanged(path);
                watcher.Renamed += (_, _) => OnFileChanged(path);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _log.Warning(ex, "could not watch config directory {Dir}", directory);
            }
        }
        return new Layer(name, () => LoadFile(path), watcher);
    }

    /// <summary>Force a re-read of all layers and recompute the merged config.</summary>
    public void Reload()
    {
        Layer[] snapshot;
        lock (_gate)
        {
            snapshot = _layers.ToArray();
        }

        var configs = new ReaderConfig?[snapshot.Length];
        for (var i = 0; i < snapshot.Length; i++)
        {
            try
            {
                configs[i] = snapshot[i].Read();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _log.Warning(ex, "layer {Layer} threw while loading; treating as empty", snapshot[i].Name);
                configs[i] = null;
            }
        }

        var merged = ConfigMerger.Merge(configs);
        bool changed;
        lock (_gate)
        {
            changed = !ReferenceEquals(_current, merged) && !_current.Equals(merged);
            _current = merged;
        }

        if (changed)
        {
            _log.Information("config reloaded ({Layers} layers)", snapshot.Length);
            try
            {
                Changed?.Invoke(merged);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _log.Warning(ex, "Changed subscriber threw");
            }
        }
    }

    public void Dispose()
    {
        Layer[] snapshot;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            snapshot = _layers.ToArray();
            _layers.Clear();
        }
        foreach (var layer in snapshot)
        {
            layer.Watcher?.Dispose();
        }
        _reloadTimer.Dispose();
    }

    private static ReaderConfig? LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        var json = File.ReadAllText(path);
        return ConfigSerializer.Deserialize(json);
    }

    private void OnFileChanged(string path)
    {
        // FileSystemWatcher fires multiple events per save (write, attribute,
        // security descriptor, etc.). Reset a Timer rather than sleeping a
        // thread-pool worker — the timer firing schedules one Reload() on the
        // pool, no matter how many watcher events arrive in the debounce window.
        _log.Verbose("config file changed: {Path}", path);
        try
        {
            _reloadTimer.Change(FileWatchDebounce, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Race with Dispose — a final reload is unnecessary.
        }
    }

    private sealed record Layer(string Name, Func<ReaderConfig?> Read, FileSystemWatcher? Watcher);
}
