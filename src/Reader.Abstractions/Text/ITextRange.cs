namespace OpenReader.Abstractions.Text;

/// <summary>
/// A movable, comparable span over a text surface. This is the type every
/// text-driven behaviour in the reader is expressed in: caret following, review
/// navigation, say-all, selection reporting, and (later) browse mode and
/// braille windows.
/// </summary>
/// <remarks>
/// <para>
/// Ranges are <em>mutable positions</em>, not value snapshots — <see cref="Move"/>
/// and <see cref="ExpandToUnit"/> change the receiver. Use <see cref="Clone"/>
/// before probing so you don't disturb a range you still need. This matches
/// UIA <c>ITextRangeProvider</c> and NVDA's <c>TextInfo</c>; deviating from it
/// forces every backend to allocate on every probe.
/// </para>
/// <para>
/// A range remains meaningful only while its surface is alive. Once the
/// underlying control is gone, methods return empty / zero rather than throw:
/// callers on the speech path must never have to guard every read.
/// </para>
/// </remarks>
public interface ITextRange
{
    /// <summary>An independent copy positioned identically to this range.</summary>
    ITextRange Clone();

    /// <summary>True when both endpoints coincide (a caret, not a selection).</summary>
    bool IsCollapsed { get; }

    /// <summary>
    /// The text spanned by this range, capped at <paramref name="maxLength"/>
    /// characters (-1 for no cap). Backends must honour the cap — an
    /// uncapped read of a large document is a hang on the speech path.
    /// </summary>
    string GetText(int maxLength = -1);

    /// <summary>
    /// Move the whole (collapsed) range by <paramref name="count"/> units.
    /// Returns the number of units actually moved, which is less than
    /// requested at a document boundary. A non-collapsed range collapses to
    /// its start first, per UIA semantics.
    /// </summary>
    int Move(TextUnit unit, int count);

    /// <summary>
    /// Move one endpoint by <paramref name="count"/> units. Returns units
    /// actually moved. If the moved endpoint crosses the other, the range
    /// collapses at the new position.
    /// </summary>
    int MoveEndpoint(RangeEndpoint endpoint, TextUnit unit, int count);

    /// <summary>
    /// Grow this range to span the whole unit containing it (the enclosing
    /// word, line, paragraph...).
    /// </summary>
    void ExpandToUnit(TextUnit unit);

    /// <summary>Collapse to a single point at the start or end endpoint.</summary>
    void Collapse(bool toStart);

    /// <summary>
    /// Move <paramref name="endpoint"/> of this range to
    /// <paramref name="targetEndpoint"/> of <paramref name="target"/>.
    /// </summary>
    void SetEndpoint(RangeEndpoint endpoint, ITextRange target, RangeEndpoint targetEndpoint);

    /// <summary>
    /// Document-order comparison of two endpoints: negative if this one comes
    /// first, zero if they coincide, positive if it comes later. Returns zero
    /// for ranges on different surfaces — callers must not compare across
    /// surfaces.
    /// </summary>
    int CompareEndpoints(RangeEndpoint endpoint, ITextRange other, RangeEndpoint otherEndpoint);

    /// <summary>
    /// Formatting and semantic attributes over this range — heading level,
    /// link target, bold, language, list depth, spelling error. Keys are the
    /// well-known names in <see cref="TextAttributes"/>. A value is present
    /// only when it is uniform across the whole range.
    /// </summary>
    IReadOnlyDictionary<string, object?> GetAttributes();
}
