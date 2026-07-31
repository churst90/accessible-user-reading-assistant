using OpenReader.Abstractions.Input;
using OpenReader.Diagnostics;
using OpenReader.Input.Commands;
using Serilog;

namespace OpenReader.Input.Gestures;

/// <summary>
/// Resolves <see cref="RawInput"/> events to <see cref="ReaderCommand"/>s using
/// a configurable chord-to-command map.
/// </summary>
/// <remarks>
/// The map is a flat dictionary; layered configuration (defaults overridden by
/// user / app rules) is built up at the host by feeding multiple
/// <see cref="Bind"/> calls in priority order — last write wins.
/// </remarks>
public sealed class GestureMap
{
    private readonly Dictionary<KeyChord, ReaderCommand> _bindings = new();
    private readonly object _gate = new();
    private readonly ILogger _log;

    public GestureMap()
    {
        _log = LoggerFactory.ForComponent("Input.GestureMap");
    }

    public int Count
    {
        get { lock (_gate) { return _bindings.Count; } }
    }

    /// <summary>Bind a chord to a command. Replaces any existing binding.</summary>
    public void Bind(KeyChord chord, ReaderCommand command)
    {
        lock (_gate)
        {
            _bindings[chord] = command;
        }
    }

    /// <summary>Remove a binding. Returns true if a binding was removed.</summary>
    public bool Unbind(KeyChord chord)
    {
        lock (_gate)
        {
            return _bindings.Remove(chord);
        }
    }

    /// <summary>Resolve a raw input to a command. Returns <see cref="ReaderCommand.None"/> on no match.</summary>
    public ReaderCommand Resolve(RawInput input)
    {
        if (input.Kind != InputEventKind.KeyDown)
        {
            return ReaderCommand.None;
        }
        var chord = new KeyChord(input.KeyCode, input.Modifiers);
        lock (_gate)
        {
            if (_bindings.TryGetValue(chord, out var command))
            {
                _log.Verbose("chord vk=0x{Vk:X2} mods={Mods} → {Command}", input.KeyCode, input.Modifiers, command);
                return command;
            }
        }
        return ReaderCommand.None;
    }

    /// <summary>Snapshot the current bindings — useful for the "what's bound to X?" debug command.</summary>
    public IReadOnlyDictionary<KeyChord, ReaderCommand> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<KeyChord, ReaderCommand>(_bindings);
        }
    }
}
