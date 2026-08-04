namespace Aura.Abstractions.Output;

/// <summary>
/// Turns a <see cref="Presentation"/> into something an output device
/// understands.
/// </summary>
/// <remarks>
/// <para>
/// There are three, and the third is the reason the other two can be trusted:
/// </para>
/// <list type="bullet">
///   <item><c>SpeechRenderer</c> → <see cref="Utterance"/></item>
///   <item><c>BrailleRenderer</c> → a braille line and its cursor routing table</item>
///   <item><c>TranscriptRenderer</c> → one deterministic line of text, which is
///   what the golden-transcript tests assert on. It is a pure function with no
///   engine, no timing and no Windows, which is what makes announcement
///   behaviour testable at all.</item>
/// </list>
/// </remarks>
public interface IPresentationRenderer<out T>
{
    /// <summary>Render, or return the type's empty value when there is nothing to convey.</summary>
    T Render(Presentation presentation);
}
