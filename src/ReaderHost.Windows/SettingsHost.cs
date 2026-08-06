using System.Runtime.Versioning;
using System.Windows.Threading;
using Aura.Abstractions.Speech;
using Aura.Config;
using Aura.UI.Settings;
using Serilog;

namespace Aura.Host;

/// <summary>
/// Single-window owner for the WPF settings dialog. Marshals
/// <c>ReaderCommand.OpenSettings</c> dispatches onto the UI thread,
/// constructs the window, persists the user's edits to disk, and lets
/// <see cref="ConfigStore"/>'s file watcher pick the change up.
/// </summary>
[SupportedOSPlatform("windows6.1")]
internal sealed class SettingsHost
{
    private readonly ConfigStore _store;
    private readonly ISpeechEngine _engine;
    private readonly Func<IReadOnlyList<string>> _engineIds;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger _log;
    private SettingsWindow? _open;

    public SettingsHost(
        ConfigStore store,
        ISpeechEngine engine,
        Dispatcher dispatcher,
        ILogger log,
        Func<IReadOnlyList<string>>? engineIds = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _engineIds = engineIds ?? Array.Empty<string>;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _log = log;
    }

    public void Show()
    {
        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_open is { IsLoaded: true })
                {
                    _open.Activate();
                    return;
                }

                // Voices come from whichever engine is currently active. The
                // EngineRouter forwards Voices to the inner engine, so picking
                // a different synth in the synth dialog auto-refreshes the
                // voice list next time Settings is opened.
                var voices = _engine.Voices.Select(v => v.Id).ToArray();
                var window = new SettingsWindow(_store.Current, voices, OnSave, _engineIds());
                window.Closed += (_, _) => _open = null;
                _open = window;
                window.Show();
                ForegroundWindowHelper.BringToFront(window);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _log.Warning(ex, "could not open settings window");
            }
        });
    }

    private void OnSave(ReaderConfig updated)
    {
        try
        {
            ConfigWriter.WriteToFile(ConfigPaths.UserConfigPath, updated);
            // Reload explicitly rather than waiting for the file watcher.
            // The watcher round-trip has too many ways to silently not happen
            // — a debounce that swallows the event, an atomic replace the
            // watcher reports as a rename it is not filtering for, a first run
            // where the directory did not exist when the watcher was created.
            // Any one of those looks identical to the user: "Apply did
            // nothing". Reloading here makes the save take effect regardless,
            // and the watcher stays as the path for edits made outside the app.
            _store.Reload();
            _log.Information("settings saved to {Path} and reloaded", ConfigPaths.UserConfigPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _log.Error(ex, "could not write user config");
        }
    }
}
