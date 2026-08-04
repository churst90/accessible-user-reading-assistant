using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Speech;
using Aura.Abstractions.Text;

namespace Aura.Abstractions.Output;

/// <summary>
/// One piece of an announcement: some text, and what that text <em>is</em>.
/// </summary>
/// <param name="Text">
/// The words. For <see cref="SegmentKind.Cue"/> this is a cue id instead.
/// </param>
/// <param name="Kind">What this segment is, for verbosity filtering.</param>
/// <param name="Role">The role it describes, when it describes one.</param>
/// <param name="Attributes">
/// Text attributes uniform across this segment — language, heading level, link
/// target. <see cref="Text.TextAttributes"/> has the well-known keys. This is
/// what lets a renderer switch voice mid-announcement.
/// </param>
/// <param name="Source">
/// Where this text came from, when it came from a text surface. Braille uses it
/// for cursor routing and say-all uses it to know where it has got to.
/// <b>Must be a bookmark-like range, not a live COM-backed one</b> — a queued
/// announcement can outlive the element it describes. See
/// <c>docs/foundation/F4-LIVENESS.md</c>.
/// </param>
public sealed record PresentationSegment(
    string Text,
    SegmentKind Kind,
    AccessibleRole? Role = null,
    IReadOnlyDictionary<string, object?>? Attributes = null,
    ITextRange? Source = null)
{
    /// <summary>
    /// True when this segment contributes nothing audible. A cue is never
    /// blank — it has no words by definition, but it is the whole point of the
    /// segment.
    /// </summary>
    public bool IsBlank => Kind is not SegmentKind.Cue && Blank.Is(Text);
}

/// <summary>
/// A composed announcement, before any decision about how to convey it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the output of the rule engine and the input to every renderer.</b>
/// Speech, braille and the test transcript all render from it, which is the
/// point: NVDA has two independent renderers that each decide what a control's
/// presentation is, with different rules, and they disagree in small ways
/// permanently because there is no single definition to reconcile them
/// against.
/// </para>
/// <para>
/// The invariant: <em>nothing constructs output text.</em> Everything
/// constructs one of these.
/// </para>
/// </remarks>
/// <param name="Segments">The pieces, in the order they should be conveyed.</param>
/// <param name="Reason">Why this announcement exists.</param>
/// <param name="Subject">
/// What it is about — normally a node id. Announcements about different
/// subjects never suppress each other, however close together they arrive.
/// </param>
/// <param name="Priority">Queue priority.</param>
/// <param name="CancelGroup">
/// Tag for stale-utterance cancellation. Pending items in the same group are
/// cancelled when a new one arrives.
/// </param>
/// <param name="Validity">
/// Whether this is still worth conveying, asked at the moment it would be.
/// <c>null</c> means unconditionally valid, which is right for anything the
/// user pressed a key to hear.
/// </param>
/// <param name="RuleTrace">
/// Ordered ids of the rules that produced this. The raw material for the
/// "why did it say that?" command, and the thing no other screen reader can
/// answer.
/// </param>
public sealed record Presentation(
    IReadOnlyList<PresentationSegment> Segments,
    SpeechReason Reason,
    string? Subject,
    SpeechPriority Priority,
    string? CancelGroup,
    IValidityPredicate? Validity,
    IReadOnlyList<string> RuleTrace)
{
    /// <summary>
    /// Voice prosody the rules asked for. A <em>hint</em>: renderers that have
    /// no voice — braille, the test transcript — ignore it.
    /// </summary>
    public ProsodyHint Prosody { get; init; } = ProsodyHint.Default;

    /// <summary>Voice the rules asked for, or <c>null</c> for the configured default. Also a hint.</summary>
    public string? VoiceId { get; init; }

    /// <summary>
    /// True when nothing in the whole presentation is audible.
    /// </summary>
    /// <remarks>
    /// The rule that depends on this: say "blank" only when this is true, and
    /// never during say-all. An empty line inside a list item is not blank —
    /// the list item is audible — which is exactly the distinction a
    /// text-versus-last-text comparison cannot make.
    /// </remarks>
    public bool IsBlank
    {
        get
        {
            for (var i = 0; i < Segments.Count; i++)
            {
                if (!Segments[i].IsBlank)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>True when there is nothing here at all.</summary>
    public bool IsEmpty => Segments.Count == 0;
}
