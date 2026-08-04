using Aura.Abstractions.Output;
using Aura.Abstractions.Speech;
using Aura.Abstractions.Text;
using Aura.Speech.Punctuation;

namespace Aura.Speech.Rendering;

/// <summary>
/// Renders a <see cref="Presentation"/> into an <see cref="Utterance"/>.
/// </summary>
/// <remarks>
/// <para>
/// Everything about <em>how words are said</em> lives here, in one place, and
/// nowhere else. That is the point of the seam: in NVDA the same decisions are
/// spread across <c>speech.py</c> and re-derived at each call site, which is
/// why its speech and braille output disagree in small ways permanently.
/// </para>
/// </remarks>
public sealed class SpeechRenderer : IPresentationRenderer<Utterance>
{
    /// <summary>Punctuation verbosity applied to spoken content.</summary>
    public PunctuationLevel PunctuationLevel { get; set; } = PunctuationLevel.Some;

    /// <summary>
    /// Capital-letter strategy: <c>"off"</c> | <c>"pitch"</c> | <c>"beep"</c> |
    /// <c>"both"</c>. <c>"beep"</c> needs the audio mixer (4b) and degrades to
    /// no cue until then.
    /// </summary>
    public string CapitalLetterAnnouncement { get; set; } = "pitch";

    /// <summary>Pitch bump, in semitones, for a single capital letter.</summary>
    public float CapitalPitchDelta { get; set; } = 6f;

    /// <summary>
    /// What is said when an announcement turns out to carry nothing audible.
    /// </summary>
    public string BlankWord { get; set; } = "blank";

    /// <summary>
    /// Whether to say <see cref="BlankWord"/> for a blank presentation.
    /// </summary>
    /// <remarks>
    /// Off by default because <c>CaretFollowService</c> still injects the word
    /// upstream; two producers of "blank" would say it twice. Turning this on
    /// and removing the upstream injection is the correct end state — blankness
    /// is a property of the composed presentation, not of one string — and it
    /// wants a listening session before it flips.
    /// </remarks>
    public bool AnnounceBlank { get; set; }

    /// <inheritdoc />
    public Utterance Render(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var parts = new List<OutputPart>(presentation.Segments.Count * 2);
        string? currentLanguage = null;
        var wroteAnything = false;

        foreach (var segment in presentation.Segments)
        {
            if (segment.Kind == SegmentKind.Cue)
            {
                parts.Add(new CuePart(segment.Text));
                wroteAnything = true;
                continue;
            }

            var text = segment.Kind == SegmentKind.Content
                ? PunctuationFilter.Apply(segment.Text, PunctuationLevel)
                : segment.Text;

            if (text.Length == 0)
            {
                continue;
            }

            // The reader's own words — role, state, position, structure — are
            // always in the reader's language, never the document's. Without
            // this, "button" gets a French accent inside a French page.
            var wanted = IsDocumentText(segment.Kind) ? LanguageOf(segment) : null;
            if (!string.Equals(wanted, currentLanguage, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(new LanguagePart(wanted));
                currentLanguage = wanted;
            }

            if (wroteAnything)
            {
                parts.Add(new TextPart(TranscriptRenderer.SegmentJoin));
            }

            var capitalCue = ShouldRaiseForCapital(text);
            if (capitalCue)
            {
                parts.Add(new ProsodyPush(new ProsodyHint(PitchDelta: CapitalPitchDelta)));
            }
            parts.Add(new TextPart(text));
            if (capitalCue)
            {
                parts.Add(new ProsodyPop());
            }

            wroteAnything = true;
        }

        if (currentLanguage is not null)
        {
            parts.Add(new LanguagePart(null));
        }

        // The blank rule, applied last and over the whole thing. An empty line
        // inside a list item is not blank — the list item is audible — which is
        // the distinction a comparison against the previous announcement's text
        // can never make.
        if (AnnounceBlank && !wroteAnything && presentation.IsBlank && presentation.Reason != SpeechReason.ReadAll)
        {
            parts.Add(new TextPart(BlankWord));
        }

        return new Utterance(
            Parts: parts,
            Priority: presentation.Priority,
            CancelGroup: presentation.CancelGroup,
            Validity: presentation.Validity,
            RuleTrace: presentation.RuleTrace)
        {
            Prosody = presentation.Prosody,
            VoiceId = presentation.VoiceId,
        };
    }

    private static bool IsDocumentText(SegmentKind kind) =>
        kind is SegmentKind.Content or SegmentKind.Name or SegmentKind.Value or SegmentKind.Description;

    private static string? LanguageOf(PresentationSegment segment) =>
        segment.Attributes is not null
        && segment.Attributes.TryGetValue(TextAttributes.Language, out var v)
        && v is string tag
        && tag.Length > 0
            ? tag
            : null;

    /// <summary>
    /// A lone uppercase letter gets a pitch bump so "A" and "a" are
    /// distinguishable. Announcing "cap" instead costs a whole word per
    /// character, which is unusable at reading speed.
    /// </summary>
    private bool ShouldRaiseForCapital(string text)
    {
        if (text.Length != 1 || !char.IsUpper(text[0]))
        {
            return false;
        }
        var mode = CapitalLetterAnnouncement;
        return string.Equals(mode, "pitch", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "both", StringComparison.OrdinalIgnoreCase);
    }
}
