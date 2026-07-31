using OpenReader.Abstractions.Accessibility;

namespace OpenReader.Abstractions.Speech;

/// <summary>
/// A single composable rule applied during speech composition. Ordered by
/// (layer, priority); higher priority within a layer wins.
/// </summary>
public sealed record SpeechRule(
    string Id,
    int Priority,
    SpeechRuleScope Scope,
    SpeechRuleAction Action);

/// <summary>
/// Predicate for whether a rule applies to a given <see cref="SpeechRequest"/>.
/// All non-null fields are AND-combined; null fields are wildcards.
/// </summary>
public sealed record SpeechRuleScope(
    AccessibleRole? Role = null,
    AccessibleStates RequiredStates = AccessibleStates.None,
    AccessibleStates ForbiddenStates = AccessibleStates.None,
    SpeechReason? Reason = null,
    string? AppExecutableName = null,
    string? TextRegex = null);

/// <summary>What a matching rule does to the in-progress utterance.</summary>
public abstract record SpeechRuleAction
{
    /// <summary>Produce text via a template. Tokens like <c>{name}</c>, <c>{role}</c>, <c>{value}</c> are substituted from the node.</summary>
    public sealed record Emit(string Template) : SpeechRuleAction;

    /// <summary>Regex-rewrite the accumulated text.</summary>
    public sealed record Rewrite(string Pattern, string Replacement) : SpeechRuleAction;

    /// <summary>Adjust prosody for this utterance.</summary>
    public sealed record Modulate(ProsodyHint Prosody) : SpeechRuleAction;

    /// <summary>Switch voice for this utterance.</summary>
    public sealed record SetVoice(string VoiceId) : SpeechRuleAction;

    /// <summary>Suppress this utterance entirely; ends rule evaluation.</summary>
    public sealed record Suppress : SpeechRuleAction;
}
