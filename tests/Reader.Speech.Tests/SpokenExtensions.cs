using Aura.Abstractions.Output;
using Aura.Speech.Rendering;

namespace Aura.Speech.Tests;

/// <summary>
/// Reads what an announcement would actually say.
/// </summary>
/// <remarks>
/// Tests assert through the transcript renderer rather than on a text property,
/// because after F1 there is no text property — an announcement is a list of
/// segments and how it sounds is a rendering decision. Asserting through the
/// renderer is also what the golden-transcript harness does, so a test here and
/// a transcript file are measuring the same thing.
/// </remarks>
internal static class SpokenExtensions
{
    /// <summary>What this presentation would say.</summary>
    /// <remarks>
    /// Throws rather than returning empty when nothing was composed. A silent
    /// empty string would make <c>NotContain</c> assertions pass vacuously,
    /// which is the one way a test can be worse than no test.
    /// </remarks>
    public static string Spoken(this Presentation? presentation)
        => presentation is null
            ? throw new InvalidOperationException("nothing was composed")
            : TranscriptRenderer.Instance.Render(presentation);

    /// <summary>The words of this utterance, with the non-verbal parts dropped.</summary>
    public static string Spoken(this Utterance? utterance)
        => utterance is null
            ? throw new InvalidOperationException("nothing was queued")
            : utterance.PlainText();
}
