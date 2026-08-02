using System.Runtime.Versioning;
using System.Windows.Threading;
using OpenReader.Abstractions.Speech;
using OpenReader.Config;
using OpenReader.UI.Dialogs;
using Serilog;

namespace OpenReader.Host;

/// <summary>
/// Single-window owner for the synthesizer-selection dialog. Lists every
/// known <see cref="ISpeechEngine"/> adapter and persists the user's choice
/// to <c>Speech.Engine</c> in the user-layer config; <see cref="ConfigStore"/>
/// reload picks the change up. Today only SAPI5 ships, so the dialog is
/// effectively forward-looking — but the chord and persistence path are
/// in place so adding eSpeak-NG (Phase 4a) is a one-line registration.
/// </summary>
[SupportedOSPlatform("windows6.1")]
internal sealed class SynthesizerDialogHost
{
    private readonly ConfigStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly Func<IReadOnlyList<SynthesizerOption>> _enginesFactory;
    private readonly ILogger _log;
    private readonly Action<string>? _announce;
    private SynthesizerDialog? _open;

    public SynthesizerDialogHost(
        ConfigStore store,
        Dispatcher dispatcher,
        Func<IReadOnlyList<SynthesizerOption>> enginesFactory,
        Action<string>? announce,
        ILogger log)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _enginesFactory = enginesFactory ?? throw new ArgumentNullException(nameof(enginesFactory));
        _announce = announce;
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

                var options = _enginesFactory();
                var current = _store.Current.Speech?.Engine ?? "sapi5";
                var window = new SynthesizerDialog(options, current);
                window.Closed += (_, _) =>
                {
                    _open = null;
                    if (window.Confirmed && window.SelectedEngineId is { } selected)
                    {
                        Persist(selected);
                    }
                };
                _open = window;
                window.Show();
                ForegroundWindowHelper.BringToFront(window);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _log.Warning(ex, "could not open synthesizer dialog");
            }
        });
    }

    private void Persist(string engineId)
    {
        try
        {
            var current = _store.Current;
            var speech = current.Speech ?? SpeechConfig.Defaults();
            var updated = current with { Speech = speech with { Engine = engineId } };
            ConfigWriter.WriteToFile(ConfigPaths.UserConfigPath, updated);
            _log.Information("synthesizer set to {Engine}", engineId);
            _announce?.Invoke($"synthesizer {engineId}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _log.Warning(ex, "could not persist synthesizer choice");
        }
    }
}
