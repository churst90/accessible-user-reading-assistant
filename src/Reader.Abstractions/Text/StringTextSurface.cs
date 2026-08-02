using System.Globalization;
using OpenReader.Abstractions.Accessibility;

namespace OpenReader.Abstractions.Text;

/// <summary>
/// An <see cref="ITextSurface"/> over a plain string plus a caret offset.
/// </summary>
/// <remarks>
/// <para>
/// Three jobs, in order of importance:
/// </para>
/// <list type="number">
///   <item><b>It is the executable specification.</b> Every other backend
///   (UIA <c>TextPattern</c>, Win32 <c>EM_*</c>, a browse-mode virtual buffer)
///   must behave the way this one does. The unit tests over this type are the
///   conformance suite.</item>
///   <item><b>It makes text behaviour testable without Windows.</b> Caret
///   following, review navigation and say-all can be driven from a synthetic
///   document in a plain unit test — which is exactly the class of bug the
///   current design can only find by running the exe.</item>
///   <item><b>It is the adapter for every "whole text plus an offset"
///   source.</b> A classic Win32 edit answers <c>WM_GETTEXT</c> and
///   <c>EM_GETSEL</c>; wrap those two reads in this type and the control gets
///   full range semantics for free, instead of the open-coded
///   <c>EM_LINEFROMCHAR</c> / <c>EM_GETLINE</c> arithmetic currently spread
///   through <c>CaretLineTracker</c>.</item>
/// </list>
/// <para>
/// Offsets are UTF-16 indices; movement by <see cref="TextUnit.Character"/> is
/// grapheme-aware, so an emoji or a combining sequence counts as one character
/// rather than two or three.
/// </para>
/// <para>
/// Not thread-safe. Callers own the synchronisation, as they do for the
/// underlying control.
/// </para>
/// </remarks>
public sealed class StringTextSurface : ITextSurface
{
    private string _text;
    private int _caret;
    private int _selectionAnchor;

    public StringTextSurface(string text, int caretOffset = 0, NodeId nodeId = default)
    {
        _text = text ?? string.Empty;
        _caret = Clamp(caretOffset, _text.Length);
        _selectionAnchor = _caret;
        NodeId = nodeId;
    }

    public NodeId NodeId { get; }

    /// <summary>The backing text. Setting it re-clamps the caret and selection.</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            _caret = Clamp(_caret, _text.Length);
            _selectionAnchor = Clamp(_selectionAnchor, _text.Length);
        }
    }

    /// <summary>Caret offset in UTF-16 code units. Setting it clears any selection.</summary>
    public int CaretOffset
    {
        get => _caret;
        set
        {
            _caret = Clamp(value, _text.Length);
            _selectionAnchor = _caret;
        }
    }

    /// <summary>Place a selection between two offsets. The caret sits at <paramref name="active"/>.</summary>
    public void Select(int anchor, int active)
    {
        _selectionAnchor = Clamp(anchor, _text.Length);
        _caret = Clamp(active, _text.Length);
    }

    // Sentence and Paragraph degrade to Line; Page degrades to Document. A
    // plain string carries no layout, so claiming native support would lie to
    // callers that branch on this.
    public bool SupportsUnit(TextUnit unit) => unit
        is TextUnit.Character or TextUnit.Word or TextUnit.Line or TextUnit.Document;

    public ITextRange? GetCaret() => new StringTextRange(this, _caret, _caret);

    public ITextRange? GetSelection()
    {
        var start = Math.Min(_selectionAnchor, _caret);
        var end = Math.Max(_selectionAnchor, _caret);
        return new StringTextRange(this, start, end);
    }

    public ITextRange GetDocumentRange() => new StringTextRange(this, 0, _text.Length);

    /// <summary>A range over an explicit offset span. Mostly useful to tests.</summary>
    public ITextRange RangeFromOffsets(int start, int end)
    {
        var s = Clamp(start, _text.Length);
        var e = Clamp(end, _text.Length);
        return new StringTextRange(this, Math.Min(s, e), Math.Max(s, e));
    }

    internal string Buffer => _text;

    private static int Clamp(int value, int length) => value < 0 ? 0 : value > length ? length : value;

    // ---- unit boundary arithmetic -------------------------------------------------
    // Shared by StringTextRange. Kept here so the boundary rules live in one
    // place: a backend that disagrees with these is a backend with a bug.

    /// <summary>
    /// Start offset of the grapheme cluster following <paramref name="offset"/>,
    /// or the text length at the end.
    /// </summary>
    internal int NextCharacter(int offset)
    {
        if (offset >= _text.Length)
        {
            return _text.Length;
        }
        var len = StringInfo.GetNextTextElementLength(_text, offset);
        return Math.Min(_text.Length, offset + Math.Max(1, len));
    }

    /// <summary>
    /// Start offset of the grapheme cluster preceding <paramref name="offset"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is no framework API for "previous grapheme", and probing backwards
    /// one position at a time does not work: the trailing half of a surrogate
    /// pair reports a cluster length of 1 all by itself, so the naive scan
    /// happily splits an emoji down the middle. We instead walk <em>forwards</em>
    /// from a nearby floor, which re-synchronises on real cluster boundaries.
    /// </para>
    /// <para>
    /// The <see cref="MaxClusterProbe"/> bound keeps a pathological input (a
    /// long combining-mark run) from turning one keystroke into a document
    /// scan, at the cost of possibly splitting that one cluster.
    /// </para>
    /// </remarks>
    internal int PreviousCharacter(int offset)
    {
        if (offset <= 0)
        {
            return 0;
        }
        offset = Math.Min(offset, _text.Length);

        var floor = Math.Max(0, offset - MaxClusterProbe);
        // A trailing surrogate can never begin a cluster, so starting there
        // would desynchronise the forward walk immediately.
        if (floor > 0 && char.IsLowSurrogate(_text[floor]))
        {
            floor++;
        }

        var start = floor;
        while (start < offset)
        {
            var len = Math.Max(1, StringInfo.GetNextTextElementLength(_text, start));
            if (start + len >= offset)
            {
                // This cluster either ends exactly at offset (it is the
                // previous character) or straddles it (offset was mid-cluster,
                // so snap to the cluster start).
                return start;
            }
            start += len;
        }
        return Math.Max(0, offset - 1);
    }

    private const int MaxClusterProbe = 32;

    /// <summary>Offset just past the last character of the line containing <paramref name="offset"/>, excluding the newline.</summary>
    internal int LineEnd(int offset)
    {
        var i = _text.IndexOf('\n', Clamp(offset, _text.Length));
        if (i < 0)
        {
            return _text.Length;
        }
        return i > 0 && _text[i - 1] == '\r' ? i - 1 : i;
    }

    /// <summary>Offset of the first character of the line containing <paramref name="offset"/>.</summary>
    internal int LineStart(int offset)
    {
        var from = Clamp(offset, _text.Length);
        if (from == 0 || _text.Length == 0)
        {
            return 0;
        }
        // Standing on the newline itself belongs to the line it terminates.
        var searchFrom = Math.Min(from, _text.Length - 1);
        if (_text[searchFrom] == '\n' && searchFrom > 0)
        {
            searchFrom--;
        }
        var i = _text.LastIndexOf('\n', searchFrom);
        return i < 0 ? 0 : i + 1;
    }

    /// <summary>Start offset of the line after the one containing <paramref name="offset"/>, or -1 at the last line.</summary>
    internal int NextLineStart(int offset)
    {
        var i = _text.IndexOf('\n', Clamp(offset, _text.Length));
        return i < 0 || i + 1 > _text.Length ? -1 : i + 1;
    }

    /// <summary>Start offset of the line before the one containing <paramref name="offset"/>, or -1 at the first line.</summary>
    internal int PreviousLineStart(int offset)
    {
        var start = LineStart(offset);
        return start == 0 ? -1 : LineStart(start - 1);
    }

    /// <summary>
    /// Start offset of the word containing or preceding <paramref name="offset"/>.
    /// A word is a maximal run of non-whitespace, so "don't" and "well-known"
    /// stay whole — splitting on punctuation is what makes the current word
    /// echo say "don" and "t".
    /// </summary>
    internal int WordStart(int offset)
    {
        var i = Clamp(offset, _text.Length);
        if (i >= _text.Length)
        {
            i = _text.Length;
        }
        // If we're on whitespace, there is no enclosing word; report where we are.
        if (i < _text.Length && WordBoundary.IsSeparator(_text[i]) && (i == 0 || WordBoundary.IsSeparator(_text[i - 1])))
        {
            return i;
        }
        while (i > 0 && !WordBoundary.IsSeparator(_text[i - 1]))
        {
            i--;
        }
        return i;
    }

    /// <summary>Offset just past the last character of the word at <paramref name="offset"/>.</summary>
    internal int WordEnd(int offset)
    {
        var i = Clamp(offset, _text.Length);
        while (i < _text.Length && !WordBoundary.IsSeparator(_text[i]))
        {
            i++;
        }
        return i;
    }

    /// <summary>Start offset of the next word, or -1 when none remains.</summary>
    internal int NextWordStart(int offset)
    {
        var i = WordEnd(Clamp(offset, _text.Length));
        while (i < _text.Length && WordBoundary.IsSeparator(_text[i]))
        {
            i++;
        }
        return i >= _text.Length ? -1 : i;
    }

    /// <summary>Start offset of the previous word, or -1 when none remains.</summary>
    internal int PreviousWordStart(int offset)
    {
        var i = Clamp(offset, _text.Length);
        // Step off the current word onto the whitespace before it.
        i = WordStart(i);
        i--;
        while (i >= 0 && WordBoundary.IsSeparator(_text[i]))
        {
            i--;
        }
        return i < 0 ? -1 : WordStart(i);
    }
}
