namespace OpenReader.Abstractions.Speech;

/// <summary>Priority bucket for a queued utterance.</summary>
public enum SpeechPriority
{
    /// <summary>Background only. Speak when nothing else is queued.</summary>
    Background = 0,

    /// <summary>Append to the queue and speak in order.</summary>
    Next = 1,

    /// <summary>Cancel the current utterance and speak immediately.</summary>
    Now = 2,
}

/// <summary>
/// A composed utterance ready for the speech engine. Output of the speech rule
/// pipeline; input to the speech queue and engine.
/// </summary>
/// <param name="Text">The literal text to speak. Already rule-rewritten.</param>
/// <param name="Prosody">Prosody deltas relative to engine defaults.</param>
/// <param name="VoiceId">Specific voice id to use, or null for the configured default.</param>
/// <param name="Priority">Queue priority.</param>
/// <param name="CancelGroup">
/// Tag for stale-utterance cancellation. Pending utterances in the same group
/// are cancelled when a new one with the same group is queued. Null disables.
/// </param>
/// <param name="RuleTrace">
/// Ordered ids of the rules that produced this utterance. Used for the
/// "why did it say that?" debug command.
/// </param>
public sealed record SpeechUtterance(
    string Text,
    ProsodyHint Prosody,
    string? VoiceId,
    SpeechPriority Priority,
    string? CancelGroup,
    IReadOnlyList<string> RuleTrace);
