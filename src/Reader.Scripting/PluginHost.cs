using System.Reflection;
using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Plugins;
using Aura.Abstractions.Speech;
using Aura.Diagnostics;
using Serilog;

namespace Aura.Scripting;

/// <summary>
/// Discovers, version-gates, loads, attaches, and tears down
/// <see cref="IAppModule"/>-implementing plugins.
/// </summary>
/// <remarks>
/// <para>
/// The host owns each plugin's lifecycle: scan a root directory for
/// <c>manifest.json</c> files, validate API compatibility, instantiate the
/// declared <see cref="IAppModule"/> in a collectible
/// <see cref="PluginLoadContext"/>, then drive
/// <see cref="IAppModule.OnAttachAsync"/> /
/// <see cref="IAppModule.OnDetachAsync"/> in response to focus changes.
/// </para>
/// <para>
/// Plugin-contributed <see cref="SpeechRule"/>s are exposed via
/// <see cref="CurrentRules"/>; subscribers (the host's speech pipeline) take
/// the snapshot and rebuild their rule engine when <see cref="RulesChanged"/>
/// fires.
/// </para>
/// <para>
/// Optional hot-reload watches the plugin root and re-scans on any change.
/// Recommended for development only — the cost of re-loading every plugin
/// on a stray timestamp tick is acceptable in dev but not in prod.
/// </para>
/// </remarks>
public sealed class PluginHost : IAsyncDisposable
{
    private readonly string[] _pluginsRoots;
    private readonly IAccessibilityProvider _provider;
    private readonly Func<SpeechRequest, bool> _announce;
    private readonly bool _hotReload;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, LoadedPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = new();
    private System.Threading.Timer? _reloadTimer;
    private ProcessInfo? _currentProcess;
    private bool _disposed;

    /// <summary>
    /// Construct a host watching a single plugin root. Equivalent to
    /// <see cref="PluginHost(IEnumerable{string}, IAccessibilityProvider, Func{SpeechRequest, bool}, bool)"/>
    /// with a one-element list.
    /// </summary>
    public PluginHost(
        string pluginsRoot,
        IAccessibilityProvider provider,
        Func<SpeechRequest, bool> announce,
        bool hotReload = false)
        : this(new[] { pluginsRoot ?? throw new ArgumentNullException(nameof(pluginsRoot)) },
               provider, announce, hotReload)
    {
    }

    /// <summary>
    /// Construct a host watching multiple plugin roots. The first-party
    /// app modules ship under the host install directory; user-installed
    /// plugins live under <see cref="PluginPaths.UserPluginsRoot"/>. Both
    /// are loaded into the same module table; the manifest <c>id</c> must
    /// be unique across roots (last-loaded wins, with a warning).
    /// </summary>
    public PluginHost(
        IEnumerable<string> pluginsRoots,
        IAccessibilityProvider provider,
        Func<SpeechRequest, bool> announce,
        bool hotReload = false)
    {
        ArgumentNullException.ThrowIfNull(pluginsRoots);
        _pluginsRoots = pluginsRoots.Where(r => !string.IsNullOrEmpty(r)).ToArray();
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _announce = announce ?? throw new ArgumentNullException(nameof(announce));
        _hotReload = hotReload;
        _log = LoggerFactory.ForComponent("Plugins");
    }

    /// <summary>Raised after a load, unload, attach, detach, or rule registration changes the rule list.</summary>
    public event Action? RulesChanged;

    /// <summary>Snapshot of all rules currently registered by attached plugins. Safe to retain.</summary>
    public IReadOnlyList<SpeechRule> CurrentRules
    {
        get
        {
            lock (_gate)
            {
                return _plugins.Values
                    .Where(p => p.Context is not null)
                    .SelectMany(p => p.Context!.Rules)
                    .ToArray();
            }
        }
    }

    /// <summary>Loaded plugins (whether currently attached or not).</summary>
    public IReadOnlyCollection<LoadedPluginInfo> Plugins
    {
        get
        {
            lock (_gate)
            {
                return _plugins.Values
                    .Select(p => new LoadedPluginInfo(p.Manifest, p.Directory, p.Context is not null))
                    .ToArray();
            }
        }
    }

    /// <summary>Discover and load every plugin under each configured root.</summary>
    public Task LoadAllAsync()
    {
        foreach (var root in _pluginsRoots)
        {
            if (!Directory.Exists(root))
            {
                _log.Information("plugins root {Root} does not exist; skipping", root);
                continue;
            }
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                TryLoadPlugin(dir);
            }
        }

        if (_hotReload)
        {
            StartWatchers();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Notify the host that focus has moved to a new process. Plugins whose
    /// <see cref="IAppModule.Matches"/> returns true for the new process are
    /// attached; previously-attached modules whose match no longer holds are
    /// detached.
    /// </summary>
    public async Task OnFocusChangedAsync(ProcessInfo? process, CancellationToken cancellationToken = default)
    {
        LoadedPlugin[] snapshot;
        lock (_gate)
        {
            _currentProcess = process;
            snapshot = _plugins.Values.ToArray();
        }

        var ruleSetMaybeChanged = false;

        foreach (var plugin in snapshot)
        {
            var matches = process is not null && SafeMatches(plugin.Module, process);
            var attached = plugin.Context is not null;

            if (matches && !attached)
            {
                await AttachAsync(plugin, process!, cancellationToken).ConfigureAwait(false);
                ruleSetMaybeChanged = true;
            }
            else if (!matches && attached)
            {
                await DetachAsync(plugin, cancellationToken).ConfigureAwait(false);
                ruleSetMaybeChanged = true;
            }
        }

        if (ruleSetMaybeChanged)
        {
            RaiseRulesChanged();
        }
    }

    /// <summary>
    /// Re-scan the plugin root: load any new plugins, unload any that are
    /// gone, and reload any whose assembly file timestamps changed.
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var toUnload = new List<LoadedPlugin>();
        var keepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in _pluginsRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }
                var manifest = PluginManifestFile.TryLoad(manifestPath, out _);
                if (manifest is null)
                {
                    continue;
                }
                keepIds.Add(manifest.Id);

                lock (_gate)
                {
                    if (_plugins.TryGetValue(manifest.Id, out var existing))
                    {
                        // Reload if any file under the dir is newer than what we loaded.
                        var newest = NewestWriteUtc(dir);
                        if (newest > existing.LoadedAtUtc)
                        {
                            toUnload.Add(existing);
                            _plugins.Remove(manifest.Id);
                        }
                    }
                }
            }
        }

        // Drop plugins that are no longer present on disk.
        lock (_gate)
        {
            foreach (var p in _plugins.Values.ToList())
            {
                if (!keepIds.Contains(p.Manifest.Id))
                {
                    toUnload.Add(p);
                    _plugins.Remove(p.Manifest.Id);
                }
            }
        }

        foreach (var plugin in toUnload)
        {
            await UnloadAsync(plugin, cancellationToken).ConfigureAwait(false);
        }

        // Load anything new.
        foreach (var root in _pluginsRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }
                var manifest = PluginManifestFile.TryLoad(manifestPath, out _);
                if (manifest is null)
                {
                    continue;
                }
                lock (_gate)
                {
                    if (_plugins.ContainsKey(manifest.Id))
                    {
                        continue;
                    }
                }
                TryLoadPlugin(dir);
            }
        }

        // Re-evaluate matches for the current process so reloaded plugins re-attach.
        await OnFocusChangedAsync(_currentProcess, cancellationToken).ConfigureAwait(false);
        RaiseRulesChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        foreach (var w in _watchers)
        {
            try { w.Dispose(); } catch { /* swallow */ }
        }
        _watchers.Clear();
        try { _reloadTimer?.Dispose(); } catch { /* swallow */ }

        LoadedPlugin[] all;
        lock (_gate)
        {
            all = _plugins.Values.ToArray();
            _plugins.Clear();
        }
        foreach (var plugin in all)
        {
            await UnloadAsync(plugin, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void TryLoadPlugin(string directory)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return;
        }
        var manifest = PluginManifestFile.TryLoad(manifestPath, out var error);
        if (manifest is null)
        {
            _log.Warning("plugin at {Dir} has invalid manifest: {Error}", directory, error);
            return;
        }
        if (!PluginApi.IsCompatible(System.Version.Parse(manifest.ApiVersion)))
        {
            _log.Warning("plugin {Id} targets api {Theirs}; host implements {Ours} — refusing",
                manifest.Id, manifest.ApiVersion, PluginApi.CurrentApiVersion);
            return;
        }

        var assemblyPath = Path.Combine(directory, manifest.Assembly);
        if (!File.Exists(assemblyPath))
        {
            _log.Warning("plugin {Id} declares assembly {Path} but it does not exist", manifest.Id, assemblyPath);
            return;
        }

        IAppModule module;
        PluginLoadContext context;
        try
        {
            context = new PluginLoadContext(assemblyPath, manifest.Id);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var moduleType = assembly.GetType(manifest.ModuleType, throwOnError: false);
            if (moduleType is null)
            {
                _log.Warning("plugin {Id} module type {Type} not found in {Assembly}",
                    manifest.Id, manifest.ModuleType, manifest.Assembly);
                return;
            }
            if (!typeof(IAppModule).IsAssignableFrom(moduleType))
            {
                _log.Warning("plugin {Id} type {Type} does not implement IAppModule",
                    manifest.Id, manifest.ModuleType);
                return;
            }
            module = (IAppModule)Activator.CreateInstance(moduleType)!;
        }
        catch (Exception ex) when (ex is FileLoadException or BadImageFormatException
                                  or TargetInvocationException or MissingMethodException)
        {
            _log.Warning(ex, "failed to load plugin {Id} from {Dir}", manifest.Id, directory);
            return;
        }

        var loaded = new LoadedPlugin(
            Manifest: manifest.ToManifest(),
            Directory: directory,
            ManifestFile: manifest,
            Context: null,
            LoadContext: context,
            Module: module,
            LoadedAtUtc: DateTime.UtcNow);

        lock (_gate)
        {
            _plugins[manifest.Id] = loaded;
        }
        _log.Information("loaded plugin {Id} v{Version}", manifest.Id, manifest.Version);
        if (manifest.Capabilities is { Count: > 0 } caps)
        {
            // Capability declarations are advisory today (Phase 4d will turn
            // these into host-enforced grants). Logging makes them auditable
            // even before enforcement lands.
            _log.Information("plugin {Id} declares capabilities: {Capabilities}",
                manifest.Id, string.Join(", ", caps));
        }
    }

    private async Task AttachAsync(LoadedPlugin plugin, ProcessInfo process, CancellationToken cancellationToken)
    {
        var ctx = new PluginContext(process, _provider, _announce, RaiseRulesChanged);
        try
        {
            await plugin.Module.OnAttachAsync(ctx, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warning(ex, "plugin {Id} OnAttachAsync threw", plugin.Manifest.Id);
            ctx.Dispose();
            return;
        }
        lock (_gate)
        {
            _plugins[plugin.Manifest.Id] = plugin with { Context = ctx };
        }
        _log.Debug("attached plugin {Id} to {Exe}", plugin.Manifest.Id, process.ExecutableName);
    }

    private async Task DetachAsync(LoadedPlugin plugin, CancellationToken cancellationToken)
    {
        try
        {
            await plugin.Module.OnDetachAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warning(ex, "plugin {Id} OnDetachAsync threw", plugin.Manifest.Id);
        }
        plugin.Context?.Dispose();
        lock (_gate)
        {
            // Only update the dictionary entry if the plugin is still
            // tracked — Dispose / Reload may have already removed it.
            if (_plugins.ContainsKey(plugin.Manifest.Id))
            {
                _plugins[plugin.Manifest.Id] = plugin with { Context = null };
            }
        }
        _log.Debug("detached plugin {Id}", plugin.Manifest.Id);
    }

    private async Task UnloadAsync(LoadedPlugin plugin, CancellationToken cancellationToken)
    {
        if (plugin.Context is not null)
        {
            await DetachAsync(plugin, cancellationToken).ConfigureAwait(false);
        }
        try
        {
            plugin.LoadContext.Unload();
        }
        catch (InvalidOperationException ex)
        {
            _log.Warning(ex, "plugin {Id} ALC unload threw", plugin.Manifest.Id);
        }
        _log.Information("unloaded plugin {Id}", plugin.Manifest.Id);
    }

    private void StartWatchers()
    {
        foreach (var root in _pluginsRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                };
                watcher.Changed += OnPluginsRootChanged;
                watcher.Created += OnPluginsRootChanged;
                watcher.Deleted += OnPluginsRootChanged;
                watcher.Renamed += OnPluginsRootChanged;
                _watchers.Add(watcher);
                _log.Information("plugin hot-reload enabled at {Root}", root);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _log.Warning(ex, "could not start plugin watcher at {Root}", root);
            }
        }
    }

    private void OnPluginsRootChanged(object sender, FileSystemEventArgs e)
    {
        // Coalesce bursts of file events into a single reload.
        if (_reloadTimer is null)
        {
            _reloadTimer = new System.Threading.Timer(async _ =>
            {
                try
                {
                    await ReloadAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    _log.Warning(ex, "hot-reload failed");
                }
            });
        }
        _reloadTimer.Change(dueTime: 500, period: System.Threading.Timeout.Infinite);
    }

    private void RaiseRulesChanged()
    {
        try
        {
            RulesChanged?.Invoke();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _log.Warning(ex, "RulesChanged subscriber threw");
        }
    }

    private bool SafeMatches(IAppModule module, ProcessInfo process)
    {
        try
        {
            return module.Matches(process);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _log.Warning(ex, "plugin Matches threw for {Exe}", process.ExecutableName);
            return false;
        }
    }

    private static DateTime NewestWriteUtc(string dir)
    {
        var newest = DateTime.MinValue;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var ts = File.GetLastWriteTimeUtc(f);
            if (ts > newest)
            {
                newest = ts;
            }
        }
        return newest;
    }
}

/// <summary>Read-only summary of a loaded plugin for UI / diagnostics.</summary>
public sealed record LoadedPluginInfo(AppModuleManifest Manifest, string Directory, bool IsAttached);

internal sealed record LoadedPlugin(
    AppModuleManifest Manifest,
    string Directory,
    PluginManifestFile ManifestFile,
    PluginContext? Context,
    PluginLoadContext LoadContext,
    IAppModule Module,
    DateTime LoadedAtUtc);
