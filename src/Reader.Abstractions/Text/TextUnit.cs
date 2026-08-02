namespace Aura.Abstractions.Text;

/// <summary>
/// Granularity of movement over a text surface. Mirrors the units every text
/// backend agrees on (UIA <c>TextUnit</c>, AT-SPI <c>AccessibleTextBoundary</c>,
/// and the plain-string fallback).
/// </summary>
/// <remarks>
/// A backend need not support every unit. Ask
/// <see cref="ITextSurface.SupportsUnit"/> first; a range asked to move by an
/// unsupported unit must degrade to the nearest supported one rather than
/// throw, exactly as UIA does.
/// </remarks>
public enum TextUnit
{
    /// <summary>One user-perceived character (grapheme cluster, not UTF-16 code unit).</summary>
    Character,

    /// <summary>One whitespace-delimited word. Punctuation attached to the word stays with it.</summary>
    Word,

    /// <summary>One line as laid out by the control (may be a wrapped visual line).</summary>
    Line,

    /// <summary>
    /// One sentence. No Windows backend implements this natively — UIA has no
    /// sentence unit at all — so it degrades to <see cref="Line"/> today. It
    /// stays in the enum because say-all wants sentence pacing and a virtual
    /// buffer can compute it.
    /// </summary>
    Sentence,

    /// <summary>One paragraph.</summary>
    Paragraph,

    /// <summary>One rendered page.</summary>
    Page,

    /// <summary>The entire text of the surface.</summary>
    Document,
}
