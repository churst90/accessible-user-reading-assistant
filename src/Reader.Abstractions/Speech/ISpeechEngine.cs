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

    /// <summary>Speak a single utterance. Returns when audio playback completes or is cancelled.</summary>
    ValueTask SpeakAsync(SpeechUtterance utterance, CancellationToken cancellationToken);

    /// <summary>Cancel any in-flight playback. Idempotent.</summary>
    ValueTask CancelAsync();
}
