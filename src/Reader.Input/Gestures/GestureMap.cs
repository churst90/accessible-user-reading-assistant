using Aura.Abstractions.Input;
using Aura.Diagnostics;
using Aura.Input.Commands;
using Serilog;

namespace Aura.Input.Gestures;

/// <summary>
/// Resolves <see cref="RawInput"/> events to <see cref="ReaderCommand"/>s
/// through a stack of context-scoped layers.
/// </summary>
/// <remarks>
/// <para>
/// Layers exist because a flat map cannot express Read mode. Quick navigation
/// needs <c>h</c> to mean "next heading" while reading a document and nothing
/// at all while the user types their name into a form. That is not a binding
/// conflict to resolve, it is two bindings in different contexts, and a single
/// dictionary has nowhere to put the difference.
/// </para>
/// <para>
/// Resolution walks layers from highest priority down and takes the first
/// match, so specificity wins: a Read-mode binding beats an app binding beats
/// a user override beats a built-in default. Within a layer, the last
/// <see cref="Bind(string,KeyChord,ReaderCommand)"/> wins, as before.
/// </para>
/// <para>
/// The unlayered <see cref="Bind(KeyChord,ReaderCommand)"/> and
/// <see cref="Resolve(RawInput)"/> overloads target
/// <see cref="DefaultLayer"/> and still behave exactly as they did, so
/// existing callers and the built-in bindings needed no changes.
/// </para>
/// </remarks>
public sealed class GestureMap
{
    /// <summary>Built-in bindings. Lowest priority — everything overrides these.</summary>
    public const string DefaultLayer = "default";

    /// <summary>User rebindings from config.</summary>
    public const string UserLayer = "user";

    /// <summary>Bindings that apply only while reading a document.</summary>
    public const string ReadModeLayer = "readmode";

    private const int DefaultPriority = 0;
    private const int UserPriority = 100;
    private const int AppPriority = 200;
    private const int ReadModePriority = 300;

    private sealed class Layer
    {
        public required string Name { get; init; }
        public required int Priority { get; init; }
        public Func<GestureContext, bool>? Applies { get; init; }
        public Dictionary<KeyChord, ReaderCommand> Bindings { get; } = new();
    }

    private readonly Dictionary<string, Layer> _layers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly ILogger _log;

    public GestureMap()
    {
        _log = LoggerFactory.ForComponent("Input.GestureMap");
        AddLayer(DefaultLayer, DefaultPriority);
        AddLayer(UserLayer, UserPriority);
        AddLayer(ReadModeLayer, ReadModePriority, ctx => ctx.Mode == Abstractions.Navigation.ReaderMode.Read);
    }

    /// <summary>Total bindings across every layer.</summary>
    public int Count
    {
        get { lock (_gate) { return _layers.Values.Sum(l => l.Bindings.Count); } }
    }

    /// <summary>
    /// Create a layer, or update the predicate of an existing one. Idempotent
    /// so a host can declare its layers without tracking whether it already
    /// did.
    /// </summary>
    public void AddLayer(string name, int priority, Func<GestureContext, bool>? applies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            var replacement = new Layer { Name = name, Priority = priority, Applies = applies };
            if (_layers.TryGetValue(name, out var existing))
            {
                // A re-declared layer is the same layer: keep its bindings so
                // declaring layers and populating them can happen in any order.
                foreach (var (chord, command) in existing.Bindings)
                {
                    replacement.Bindings[chord] = command;
                }
            }
            _layers[name] = replacement;
        }
    }

    /// <summary>
    /// Declare a layer scoped to one application, matched on executable name
    /// without extension, case-insensitively.
    /// </summary>
    public void AddAppLayer(string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        AddLayer(
            AppLayerName(executableName),
            AppPriority,
            ctx => string.Equals(ctx.AppExecutableName, executableName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The conventional layer name for an application.</summary>
    public static string AppLayerName(string executableName) => "app:" + executableName;

    /// <summary>Bind a chord in the default layer. Replaces any existing binding there.</summary>
    public void Bind(KeyChord chord, ReaderCommand command) => Bind(DefaultLayer, chord, command);

    /// <summary>Bind a chord within a named layer, creating the layer if needed.</summary>
    public void Bind(string layer, KeyChord chord, ReaderCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layer);
        lock (_gate)
        {
            if (!_layers.TryGetValue(layer, out var target))
            {
                target = new Layer { Name = layer, Priority = DefaultPriority };
                _layers[layer] = target;
            }
            target.Bindings[chord] = command;
        }
    }

    /// <summary>Remove a binding from the default layer.</summary>
    public bool Unbind(KeyChord chord) => Unbind(DefaultLayer, chord);

    /// <summary>Remove a binding from a named layer.</summary>
    public bool Unbind(string layer, KeyChord chord)
    {
        lock (_gate)
        {
            return _layers.TryGetValue(layer, out var target) && target.Bindings.Remove(chord);
        }
    }

    /// <summary>Drop every binding in a layer, keeping the layer itself.</summary>
    public void ClearLayer(string layer)
    {
        lock (_gate)
        {
            if (_layers.TryGetValue(layer, out var target))
            {
                target.Bindings.Clear();
            }
        }
    }

    /// <summary>Resolve using the default context — nothing known, Type mode.</summary>
    public ReaderCommand Resolve(RawInput input) => Resolve(input, GestureContext.Default);

    /// <summary>
    /// Resolve a raw input to a command, honouring layer priority and each
    /// layer's applicability. Returns <see cref="ReaderCommand.None"/> when
    /// nothing matches.
    /// </summary>
    public ReaderCommand Resolve(RawInput input, GestureContext context)
    {
        if (input.Kind != InputEventKind.KeyDown)
        {
            return ReaderCommand.None;
        }

        var chord = new KeyChord(input.KeyCode, input.Modifiers);
        lock (_gate)
        {
            foreach (var layer in ApplicableLayersDescending(context))
            {
                if (layer.Bindings.TryGetValue(chord, out var command))
                {
                    _log.Verbose("chord vk=0x{Vk:X2} mods={Mods} → {Command} [{Layer}]",
                        input.KeyCode, input.Modifiers, command, layer.Name);
                    return command;
                }
            }
        }
        return ReaderCommand.None;
    }

    /// <summary>
    /// Flattened view of what is bound in a context, highest-priority layer
    /// winning. Drives "what is this key bound to?" and the rebinding UI.
    /// </summary>
    public IReadOnlyDictionary<KeyChord, ReaderCommand> Snapshot(GestureContext context)
    {
        var result = new Dictionary<KeyChord, ReaderCommand>();
        lock (_gate)
        {
            // Ascending, so higher-priority layers overwrite.
            foreach (var layer in ApplicableLayersDescending(context).Reverse())
            {
                foreach (var (chord, command) in layer.Bindings)
                {
                    result[chord] = command;
                }
            }
        }
        return result;
    }

    /// <summary>Snapshot in the default context.</summary>
    public IReadOnlyDictionary<KeyChord, ReaderCommand> Snapshot() => Snapshot(GestureContext.Default);

    /// <summary>
    /// Every binding in every layer regardless of context, tagged with its
    /// layer. Used by documentation generation and conflict reporting, which
    /// must see bindings that no current context activates.
    /// </summary>
    public IReadOnlyList<(string Layer, KeyChord Chord, ReaderCommand Command)> SnapshotAllLayers()
    {
        lock (_gate)
        {
            return _layers.Values
                .OrderByDescending(l => l.Priority)
                .ThenBy(l => l.Name, StringComparer.Ordinal)
                .SelectMany(l => l.Bindings.Select(b => (l.Name, b.Key, b.Value)))
                .ToList();
        }
    }

    private IEnumerable<Layer> ApplicableLayersDescending(GestureContext context)
        => _layers.Values
            .Where(l => l.Applies is null || l.Applies(context))
            .OrderByDescending(l => l.Priority)
            .ThenBy(l => l.Name, StringComparer.Ordinal);
}
