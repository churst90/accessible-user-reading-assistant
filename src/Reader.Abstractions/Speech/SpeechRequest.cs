using OpenReader.Abstractions.Accessibility;

namespace OpenReader.Abstractions.Speech;

/// <summary>
/// Input to the speech rule pipeline. Combines the source event, the focused
/// or referenced node, and any literal text the rule engine should compose with.
/// </summary>
public sealed record SpeechRequest(
    SpeechReason Reason,
    AccessibleNode? Node,
    string? RawText,
    string? AppExecutableName,
    IReadOnlyDictionary<string, object?>? Extras = null);
