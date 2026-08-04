using Aura.Abstractions.Speech;

namespace Aura.Abstractions.Output;

/// <summary>
/// A rendered announcement, ready for a speech engine.
/// </summary>
/// <remarks>
/// Produced by a speech renderer from a <see cref="Presentation"/>. The
/// <see cref="Presentation"/> says what should be conveyed; this says how to
/// say it.
/// </remarks>
public sealed record Utterance(
    IReadOnlyList<OutputPart> Parts,
    SpeechPriority Priority,
    string? CancelGroup,
    IValidityPredicate? Validity,
    IReadOnlyList<string> RuleTrace)
{
    /// <summary>
    /// Voice settings for the utterance as a whole.
    /// </summary>
    /// <remarks>
    /// Not a second mechanism competing with <see cref="ProsodyPush"/>: this is
    /// the baseline the voice is set to before speaking, and pushes are
    /// deviations within it. Engines split the same way — SAPI sets a rate on
    /// the voice and marks up spans inline — so an engine with no span support
    /// still honours the baseline.
    /// </remarks>
    public ProsodyHint Prosody { get; init; } = ProsodyHint.Default;

    /// <summary>Voice id to use, or <c>null</c> for the configured default.</summary>
    public string? VoiceId { get; init; }

    /// <summary>
    /// The words only, with everything else dropped. For logging (redacted),
    /// for engines with no sequence support, and for tests.
    /// </summary>
    public string PlainText()
    {
        if (Parts.Count == 1 && Parts[0] is TextPart only)
        {
            return only.Text;
        }
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < Parts.Count; i++)
        {
            switch (Parts[i])
            {
                case TextPart t:
                    sb.Append(t.Text);
                    break;
                case SpellPart s:
                    sb.Append(s.Text);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>True when there is nothing to say.</summary>
    public bool IsEmpty => Parts.Count == 0;
}

/// <summary>
/// Which <see cref="OutputPart"/> kinds an engine can honour. Everything else
/// it ignores.
/// </summary>
[Flags]
public enum OutputCapabilities
{
    None = 0,

    /// <summary>Mid-utterance language switching.</summary>
    Language = 1,

    /// <summary>Prosody changes part-way through an utterance.</summary>
    Prosody = 2,

    /// <summary>Explicit pauses.</summary>
    Break = 4,

    /// <summary>Position markers reported during synthesis.</summary>
    Marker = 8,

    /// <summary>Explicit phoneme pronunciation.</summary>
    Phoneme = 16,

    /// <summary>Character-by-character spelling mode.</summary>
    CharacterMode = 32,
}
