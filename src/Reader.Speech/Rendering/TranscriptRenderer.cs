using System.Text;
using Aura.Abstractions.Output;

namespace Aura.Speech.Rendering;

/// <summary>
/// Renders a <see cref="Presentation"/> to one deterministic line of text.
/// </summary>
/// <remarks>
/// <para>
/// This is the renderer the golden-transcript tests assert on, and it is why
/// the output model exists in the shape it does. It is a pure function: no
/// engine, no timing, no Windows, no audio. That is what makes the question a
/// screen reader actually needs answered — <em>given this tree and these
/// keystrokes, what did it say, in what order?</em> — a thing that can be
/// committed to git and diffed in CI.
/// </para>
/// <para>
/// It is deliberately lossy in one direction and lossless in the other: it
/// renders every segment that would be conveyed, in order, and it renders
/// non-verbal parts visibly (<c>[cue:name]</c>) rather than dropping them, so a
/// test can assert an earcon fired. It does not render prosody or voice, which
/// are hints and would make transcripts churn.
/// </para>
/// </remarks>
public sealed class TranscriptRenderer : IPresentationRenderer<string>
{
    /// <summary>The shared instance. It has no state.</summary>
    public static readonly TranscriptRenderer Instance = new();

    /// <inheritdoc />
    public string Render(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var sb = new StringBuilder();
        foreach (var segment in presentation.Segments)
        {
            if (segment.Kind == SegmentKind.Cue)
            {
                Append(sb, $"[cue:{segment.Text}]");
                continue;
            }
            if (segment.Text.Length == 0)
            {
                continue;
            }
            Append(sb, segment.Text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Render with each segment's kind attached — <c>Name(OK) Role(button)</c>.
    /// For the tests that are about the decomposition itself rather than about
    /// what the user hears.
    /// </summary>
    public static string RenderTyped(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var sb = new StringBuilder();
        foreach (var segment in presentation.Segments)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }
            sb.Append(segment.Kind).Append('(').Append(segment.Text).Append(')');
        }
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string text)
    {
        if (sb.Length > 0)
        {
            sb.Append(SegmentJoin);
        }
        sb.Append(text);
    }

    /// <summary>
    /// How segments are joined. Matches the speech renderer so a transcript
    /// reads as what was heard — if these ever diverge, the transcript stops
    /// being evidence.
    /// </summary>
    internal const string SegmentJoin = ", ";
}
