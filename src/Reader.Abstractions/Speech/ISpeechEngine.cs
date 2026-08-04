using Aura.Abstractions.Output;

namespace Aura.Abstractions.Speech;

/// <summary>
/// Adapter over a concrete TTS engine (SAPI5, eSpeak-NG, Azure, etc).
/// Engines are intentionally dumb — queueing, cancellation grouping, and
/// rule composition all live above this seam.
/// </summary>
public interface ISpeechEngine : IAsyncDisposable
{
    /// <summary>Stable engine identifier (e.g. <c>sapi5</c>, <c>espeak-ng</c>).</summary>
    string Id { get; }

    /// <summary>Voices currently exposed by the engine.</summary>
    IReadOnlyList<VoiceInfo> Voices { get; }

    /// <summary>Voice currently used when an utterance does not specify one.</summary>
    string? DefaultVoiceId { get; set; }

    /// <summary>
    /// Which <see cref="OutputPart"/> kinds this engine honours. Anything else
    /// it must silently ignore.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="OutputCapabilities.None"/> — words only — so an
    /// engine added later is correct before it is complete. Degrading is
    /// always allowed; throwing is not, because a lost part must never become
    /// a lost announcement.
    /// </remarks>
    OutputCapabilities Capabilities => OutputCapabilities.None;

    /// <summary>
    /// Raised as synthesis passes a <see cref="MarkerPart"/>, with its id.
    /// </summary>
    /// <remarks>
    /// This is how say-all knows where it has got to — so it can resume at the
    /// right word after an interruption, move the caret as it reads, and keep
    /// braille in step. SAPI 5 supplies it through bookmarks and eSpeak NG
    /// through its index callback; an engine with no equivalent never raises
    /// it.
    /// </remarks>
    event Action<int>? MarkerReached;

    /// <summary>Speak a single utterance. Returns when audio playback completes or is cancelled.</summary>
    ValueTask SpeakAsync(Utterance utterance, CancellationToken cancellationToken);

    /// <summary>Cancel any in-flight playback. Idempotent.</summary>
    ValueTask CancelAsync();
}
