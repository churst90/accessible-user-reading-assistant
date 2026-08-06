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
    /// <summary>Which parts of an announcement are conveyed at all.</summary>
    public Verbosity Verbosity { get; set; } = Verbosity.Full;

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

    // There was an AnnounceBlank switch here, defaulting to off, guarding a
    // whole-presentation blank rule that duplicated what CaretFollowService
    // already does. Two producers of the same word, one of them disabled, is
    // the shape of thing this project keeps getting caught by — it reads as a
    // feature and behaves as nothing.
    //
    // CaretFollowService owns "blank" today and it is verified by ear. Moving
    // it here is still the right end state, because blankness is a property of
    // the whole composed announcement rather than of one motion — an empty line
    // inside a list item is not blank. That move needs a listening session, not
    // a flag nobody sets.

    /// <inheritdoc />
    public Utterance Render(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var parts = new List<OutputPart>(presentation.Segments.Count * 2);
        string? currentLanguage = null;
        var wroteAnything = false;

        foreach (var segment in presentation.Segments)
        {
            if (!Verbosity.Allows(segment.Kind))
            {
                continue;
            }
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
