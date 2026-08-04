using Aura.Abstractions.Text;

namespace Aura.Core.Text;

/// <summary>
/// Works out what to announce by comparing where the caret was against where
/// it is now.
/// </summary>
/// <remarks>
/// <para>
/// This replaces guessing the announcement from the keystroke. The current
/// design classifies the key (<c>Left</c> means character, <c>Ctrl+Left</c>
/// means word, <c>Up</c> means line...), waits a fixed 15 ms for the app to
/// react, reads the caret, and suppresses the competing UIA event for 250 ms
/// so the two paths don't both speak. That has four failure modes the
/// keystroke can never account for:
/// </para>
/// <list type="bullet">
///   <item>The app disagrees. <c>Left</c> at the start of a line moves up a
///   line. <c>Ctrl+Left</c> in a terminal may do nothing. Word boundaries
///   differ per control.</item>
///   <item>The caret moves without a keystroke — a mouse click, a find
///   result, autocomplete, a screen reader's own caret command.</item>
///   <item>The fixed delay is a race. Under load the app hasn't moved yet and
///   the reader announces the position it just left.</item>
///   <item>Two producers of the same announcement have to suppress each other
///   by wall-clock window, so either can win and both can lose.</item>
/// </list>
/// <para>
/// Comparing observed positions removes all of those. But it does not remove
/// the keystroke entirely, and an earlier version of this class was wrong to
/// try. The two carry <em>different</em> information:
/// </para>
/// <list type="bullet">
///   <item><b>The positions say what happened.</b> Where the caret went, and
///   whether it went anywhere at all.</item>
///   <item><b>The keystroke says what granularity was asked for.</b> Left is a
///   character move even when it wraps to the previous line, and reporting the
///   whole line because a newline happened to be crossed is exactly the bug
///   that behaviour produced — the user asked for one character and heard a
///   paragraph.</item>
/// </list>
/// <para>
/// So a caller that knows which key was pressed passes the unit it implies, and
/// gets that unit reported. A caller that does not — a mouse click, a find
/// result, autocomplete, a caret event with no keystroke behind it — passes
/// nothing and the unit is inferred from the distance covered, which is the
/// best available answer when nobody asked for anything.
/// </para>
/// <para>
/// The caller owns the "when to sample" decision and must not diff across a
/// content change: if the text itself changed (the user typed, or an edit was
/// undone) the offsets refer to different documents and the delta is
/// meaningless. Treat that as typing and let key echo handle it.
/// </para>
/// </remarks>
public static class CaretMotionResolver
{
    /// <summary>
    /// Compare two caret/selection observations from the same surface.
    /// </summary>
    /// <param name="previous">
    /// The last observed position, or <c>null</c> if this is the first
    /// observation for this surface.
    /// </param>
    /// <param name="current">The position now, or <c>null</c> if the surface lost its caret.</param>
    /// <returns>The motion to announce. Never <c>null</c>.</returns>
    /// <param name="requested">
    /// The granularity the user asked for, when a keystroke implies one.
    /// <c>null</c> when nothing was asked and the unit has to be inferred.
    /// </param>
    public static CaretMotion Resolve(ITextRange? previous, ITextRange? current, TextUnit? requested = null)
    {
        if (current is null)
        {
            return CaretMotion.None;
        }

        // No prior observation — the caret just arrived (focus landed, or the
        // control was created). Orient the user with the whole line.
        if (previous is null)
        {
            return new CaretMotion(CaretMotionKind.Line, ReadUnit(current, TextUnit.Line));
        }

        var wasSelecting = !previous.IsCollapsed;
        var isSelecting = !current.IsCollapsed;

        if (isSelecting)
        {
            return ResolveSelection(previous, current, wasSelecting);
        }

        if (wasSelecting)
        {
            return new CaretMotion(CaretMotionKind.SelectionCleared, previous.GetText(SelectionReadCap));
        }

        return ResolveCaret(previous, current, requested);
    }

    /// <summary>
    /// The line at <paramref name="current"/>, guarding against providers that
    /// expand a blank line backwards onto the previous one.
    /// </summary>
    /// <remarks>
    /// At the start of an empty line the caret position is identical to the end
    /// of the line above, and several providers — Windows 11 Notepad among them
    /// — resolve that ambiguity the wrong way and return the previous line. The
    /// reader then repeats the line above instead of saying "blank".
    ///
    /// Position alone cannot distinguish the two cases, which is exactly why
    /// the providers get it wrong. Comparing the two <em>expansions</em> can:
    /// if expanding from the new caret yields the same range as expanding from
    /// the old one, the caret demonstrably crossed a line boundary and yet the
    /// provider handed back the line we already left. That only happens when
    /// the new line is empty.
    /// </remarks>
    private static string ReadLineAt(ITextRange current, ITextRange previous)
    {
        var here = current.Clone();
        here.Collapse(toStart: true);
        here.ExpandToUnit(TextUnit.Line);

        var there = previous.Clone();
        there.Collapse(toStart: true);
        there.ExpandToUnit(TextUnit.Line);

        var sameStart = here.CompareEndpoints(RangeEndpoint.Start, there, RangeEndpoint.Start) == 0;
        var sameEnd = here.CompareEndpoints(RangeEndpoint.End, there, RangeEndpoint.End) == 0;
        if (sameStart && sameEnd)
        {
            return string.Empty;
        }

        return here.GetText(UnitReadCap);
    }

    /// <summary>Read the text of the unit enclosing <paramref name="range"/> without disturbing it.</summary>
    public static string ReadUnit(ITextRange range, TextUnit unit)
    {
        if (range is null)
        {
            return string.Empty;
        }
        var probe = range.Clone();
        probe.Collapse(toStart: true);
        probe.ExpandToUnit(unit);

        return probe.GetText(UnitReadCap);
    }

    /// <summary>Read the text implied by a resolved motion, or the motion's own text for selection changes.</summary>
    public static string TextFor(CaretMotion motion, ITextRange current)
    {
        if (motion is null)
        {
            return string.Empty;
        }
        return motion.Unit is { } unit ? ReadUnit(current, unit) : motion.Text;
    }

    // A line can be pathologically long (a minified file on one line). Cap the
    // read so a single keystroke can't turn into a megabyte of speech.
    private const int UnitReadCap = 8192;
    private const int SelectionReadCap = 65536;

    private static CaretMotion ResolveCaret(ITextRange previous, ITextRange current, TextUnit? requested)
    {
        var order = current.CompareEndpoints(RangeEndpoint.Start, previous, RangeEndpoint.Start);
        if (order == 0)
        {
            return CaretMotion.None;
        }

        // The user asked for a granularity. Report that, whatever ground the
        // caret happened to cover getting there.
        if (requested is { } unit)
        {
            return unit switch
            {
                TextUnit.Character => new CaretMotion(CaretMotionKind.Character, ReadUnit(current, TextUnit.Character)),
                TextUnit.Word => new CaretMotion(CaretMotionKind.Word, ReadUnit(current, TextUnit.Word)),
                _ => new CaretMotion(CaretMotionKind.Line, ReadLineAt(current, previous)),
            };
        }

        // Span the ground covered, lower position to higher, and let its
        // content say what unit was crossed. This is why the classification
        // needs no key code: the text between the two positions already
        // encodes the answer.
        var span = current.Clone();
        if (order > 0)
        {
            span.SetEndpoint(RangeEndpoint.Start, previous, RangeEndpoint.Start);
        }
        else
        {
            span.SetEndpoint(RangeEndpoint.End, previous, RangeEndpoint.Start);
        }

        var crossed = span.GetText(UnitReadCap);
        if (crossed.Contains('\n') || crossed.Contains('\r'))
        {
            return new CaretMotion(CaretMotionKind.Line, ReadLineAt(current, previous));
        }

        // Exactly one grapheme crossed means a character move. Measuring in
        // graphemes rather than UTF-16 units is what keeps an emoji from
        // reading as two half-characters.
        var probe = current.Clone();
        probe.Collapse(toStart: true);
        if (IsSingleCharacterStep(crossed))
        {
            return new CaretMotion(CaretMotionKind.Character, ReadUnit(current, TextUnit.Character));
        }

        return new CaretMotion(CaretMotionKind.Word, ReadUnit(current, TextUnit.Word));
    }

    private static bool IsSingleCharacterStep(string crossed)
    {
        if (crossed.Length == 0)
        {
            return false;
        }
        var length = System.Globalization.StringInfo.GetNextTextElementLength(crossed, 0);
        return length >= crossed.Length;
    }

    private static CaretMotion ResolveSelection(ITextRange previous, ITextRange current, bool wasSelecting)
    {
        var now = current.GetText(SelectionReadCap);
        if (!wasSelecting)
        {
            return new CaretMotion(CaretMotionKind.SelectionGrew, now);
        }

        var before = previous.GetText(SelectionReadCap);
        if (string.Equals(before, now, StringComparison.Ordinal))
        {
            return CaretMotion.None;
        }

        // Shift-arrow grows or shrinks the selection at one end, so the change
        // is a common prefix or suffix away. Anything more exotic (the anchor
        // flipped) falls back to reading the new selection whole.
        if (now.Length > before.Length)
        {
            var added = Difference(before, now);
            return new CaretMotion(CaretMotionKind.SelectionGrew, added ?? now);
        }

        var removed = Difference(now, before);
        return new CaretMotion(CaretMotionKind.SelectionShrank, removed ?? before);
    }

    /// <summary>The part of <paramref name="longer"/> that <paramref name="shorter"/> lacks, when it's a clean prefix or suffix.</summary>
    private static string? Difference(string shorter, string longer)
    {
        if (longer.StartsWith(shorter, StringComparison.Ordinal))
        {
            return longer.Substring(shorter.Length);
        }
        if (longer.EndsWith(shorter, StringComparison.Ordinal))
        {
            return longer.Substring(0, longer.Length - shorter.Length);
        }
        return null;
    }
}
