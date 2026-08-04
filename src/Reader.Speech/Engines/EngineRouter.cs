using Aura.Abstractions.Output;
using Aura.Abstractions.Speech;
using Aura.Diagnostics;
using Serilog;

namespace Aura.Speech.Engines;

/// <summary>
/// <see cref="ISpeechEngine"/> wrapper that delegates to a swappable inner
/// engine. Lets the host change synthesizers at runtime without restarting —
/// the synth-selection dialog writes <c>Speech.Engine</c> to config; the host
/// reacts to <c>ConfigStore.Changed</c> by calling <see cref="Switch"/>.
/// </summary>
/// <remarks>
/// <para>
/// All forwarded calls take a snapshot of the current inner engine under a
/// lock so a swap mid-call cannot tear. The previous engine is cancelled on
/// switch (best-effort) so its in-flight utterance does not bleed into the
/// new engine's audio.
/// </para>
/// <para>
/// <see cref="Voices"/> returns the current engine's list — the settings
/// dialog refreshes its voice picker after a switch so the user sees the
/// right voices.
/// </para>
/// </remarks>
public sealed class EngineRouter : ISpeechEngine
{
    private readonly object _gate = new();
    private readonly ILogger _log;
    private ISpeechEngine _current;

    public EngineRouter(ISpeechEngine initial)
    {
        _current = initial ?? throw new ArgumentNullException(nameof(initial));
        _current.MarkerReached += OnInnerMarker;
        _log = LoggerFactory.ForComponent("Speech.EngineRouter");
    }

    /// <summary>Identifier of the currently-active engine.</summary>
    public string Id
    {
        get { lock (_gate) { return _current.Id; } }
    }

    public IReadOnlyList<VoiceInfo> Voices
    {
        get { lock (_gate) { return _current.Voices; } }
    }

    public string? DefaultVoiceId
    {
        get { lock (_gate) { return _current.DefaultVoiceId; } }
        set { lock (_gate) { _current.DefaultVoiceId = value; } }
    }

    /// <summary>Raised after a successful <see cref="Switch"/>.</summary>
    public event Action<ISpeechEngine>? EngineChanged;

    /// <summary>
    /// Replace the current engine with <paramref name="next"/>. Cancels the
    /// outgoing engine's in-flight playback so audio doesn't bleed across the
    /// transition. Returns the engine that was previously active.
    /// </summary>
    /// <remarks>
    /// Does NOT dispose the outgoing engine — the host owns engine lifetimes.
    /// We hold both engines for the session and switch between them; that
    /// keeps switch latency low (no re-initialize).
    /// </remarks>
    public ISpeechEngine Switch(ISpeechEngine next)
    {
        ArgumentNullException.ThrowIfNull(next);
        ISpeechEngine previous;
        lock (_gate)
        {
            previous = _current;
            if (ReferenceEquals(previous, next))
            {
                return previous;
            }
            previous.MarkerReached -= OnInnerMarker;
            next.MarkerReached += OnInnerMarker;
            _current = next;
        }
        // Cancel the outgoing engine outside the lock — its CancelAsync may
        // synchronously dispatch to its own internals. Convert to Task so the
        // analyzer is happy with a fire-and-forget discard; both SAPI and
        // eSpeak return synchronously-completed ValueTasks today.
        try { _ = previous.CancelAsync().AsTask(); }
        catch (Exception ex) when (!IsCritical(ex)) { _log.Warning(ex, "previous engine cancel threw on switch"); }

        _log.Information("speech engine switched: {From} -> {To}", previous.Id, next.Id);
        try { EngineChanged?.Invoke(next); }
        catch (Exception ex) when (!IsCritical(ex)) { _log.Warning(ex, "EngineChanged subscriber threw"); }
        return previous;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Re-pointed at the new engine on every <see cref="Switch"/>, so a synth
    /// swap part-way through a say-all does not silently stop reporting
    /// position — which would leave it unable to resume.
    /// </remarks>
    public event Action<int>? MarkerReached;

    /// <inheritdoc />
    public OutputCapabilities Capabilities
    {
        get { lock (_gate) { return _current.Capabilities; } }
    }

    private void OnInnerMarker(int id) => MarkerReached?.Invoke(id);

    public ValueTask SpeakAsync(Utterance utterance, CancellationToken cancellationToken)
    {
        ISpeechEngine target;
        lock (_gate) { target = _current; }
        return target.SpeakAsync(utterance, cancellationToken);
    }

    public ValueTask CancelAsync()
    {
        ISpeechEngine target;
        lock (_gate) { target = _current; }
        return target.CancelAsync();
    }

    public ValueTask DisposeAsync()
    {
        // The router does not own its inner engines; the host disposes each
        // concrete engine separately.
        return ValueTask.CompletedTask;
    }

    private static bool IsCritical(Exception ex)
        => ex is OutOfMemoryException or StackOverflowException or ThreadAbortException;
}
