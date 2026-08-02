using Aura.Abstractions.Input;
using Aura.Diagnostics;
using Aura.Input.Commands;
using Serilog;

namespace Aura.Input.Gestures;

/// <summary>
/// Subscribes to an <see cref="IInputSource"/>, resolves chords via a
/// <see cref="GestureMap"/>, and dispatches commands through a
/// <see cref="CommandBus"/>.
/// </summary>
/// <remarks>
/// Supports a "keyboard help" mode that, while active, intercepts every
/// resolved chord, announces the bound command name through a host callback
/// instead of dispatching it, and lets <see cref="ReaderCommand.ToggleKeyboardHelp"/>
/// or <see cref="ReaderCommand.StopSpeech"/> exit the mode. Useful for users
/// learning their keybindings.
/// </remarks>
public sealed class GestureRouter : IDisposable
{
    private readonly IInputSource _source;
    private readonly GestureMap _map;
    private readonly CommandBus _bus;
    private readonly ILogger _log;
    private Action<ReaderCommand>? _helpModeAnnouncer;
    private bool _helpMode;
    private bool _started;
    private bool _disposed;

    public GestureRouter(IInputSource source, GestureMap map, CommandBus bus)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _log = LoggerFactory.ForComponent("Input.GestureRouter");
    }

    /// <summary>
    /// True iff keyboard-help mode is active. Setting this directly should be
    /// rare — usually you bind <see cref="ReaderCommand.ToggleKeyboardHelp"/>
    /// to a chord and the router toggles itself.
    /// </summary>
    public bool KeyboardHelpMode
    {
        get => _helpMode;
        set => _helpMode = value;
    }

    /// <summary>
    /// Set the callback used to announce a chord's command name while in help
    /// mode. The host typically wires this to <c>SpeechPipeline.Submit</c>
    /// with the command's display label.
    /// </summary>
    public void SetHelpAnnouncer(Action<ReaderCommand>? announcer)
    {
        _helpModeAnnouncer = announcer;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        _source.RawInputReceived += OnRawInput;
        _log.Debug("gesture router subscribed");
    }

    private void OnRawInput(object? sender, RawInput input)
    {
        var command = _map.Resolve(input);
        if (command == ReaderCommand.None)
        {
            return;
        }

        if (_helpMode)
        {
            // Toggle and stop-speech are the only chords that escape help mode.
            if (command is ReaderCommand.ToggleKeyboardHelp or ReaderCommand.StopSpeech)
            {
                _helpMode = false;
                _ = DispatchAsync(command);
                return;
            }
            try
            {
                _helpModeAnnouncer?.Invoke(command);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _log.Warning(ex, "help-mode announcer threw on {Command}", command);
            }
            return;
        }

        if (command == ReaderCommand.ToggleKeyboardHelp)
        {
            _helpMode = true;
        }

        _ = DispatchAsync(command);
    }

    private async Task DispatchAsync(ReaderCommand command)
    {
        try
        {
            await _bus.DispatchAsync(command).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _log.Warning(ex, "command bus threw on {Command}", command);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _source.RawInputReceived -= OnRawInput;
    }
}
