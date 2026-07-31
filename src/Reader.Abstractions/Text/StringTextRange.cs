
namespace OpenReader.Abstractions.Text;

/// <summary>
/// <see cref="ITextRange"/> over a <see cref="StringTextSurface"/>. Semantics
/// deliberately mirror UIA <c>ITextRangeProvider</c> so the UIA backend is a
/// thin forward and this type stays the conformance reference.
/// </summary>
internal sealed class StringTextRange : ITextRange
{
    private readonly StringTextSurface _surface;
    private int _start;
    private int _end;

    internal StringTextRange(StringTextSurface surface, int start, int end)
    {
        _surface = surface;
        _start = start;
        _end = end;
        Normalize();
    }

    internal int Start => _start;

    internal int End => _end;

    public bool IsCollapsed => _start == _end;

    public ITextRange Clone() => new StringTextRange(_surface, _start, _end);

    public string GetText(int maxLength = -1)
    {
        var text = _surface.Buffer;
        Normalize();
        var length = _end - _start;
        if (length <= 0)
        {
            return string.Empty;
        }
        if (maxLength >= 0 && length > maxLength)
        {
            length = maxLength;
        }
        return text.Substring(_start, length);
    }

    /// <summary>
    /// UIA <c>Move</c> semantics: collapse to the start, step by whole units,
    /// then expand to the landing unit. Callers that want a bare point move
    /// should <see cref="Collapse"/> then <see cref="MoveEndpoint"/>.
    /// </summary>
    public int Move(TextUnit unit, int count)
    {
        if (count == 0)
        {
            return 0;
        }
        Collapse(toStart: true);
        var moved = MoveEndpoint(RangeEndpoint.Start, unit, count);
        _end = _start;
        // Expand even when we hit a boundary and moved nothing: the caller
        // learns that from the return value, and a degenerate range would make
        // "read the line you failed to leave" impossible.
        ExpandToUnit(unit);
        return moved;
    }

    public int MoveEndpoint(RangeEndpoint endpoint, TextUnit unit, int count)
    {
        if (count == 0)
        {
            return 0;
        }
        var effective = Degrade(unit);
        var offset = endpoint == RangeEndpoint.Start ? _start : _end;
        var moved = 0;
        var step = Math.Sign(count);

        for (var i = 0; i < Math.Abs(count); i++)
        {
            var next = StepOne(offset, effective, step);
            if (next < 0 || next == offset)
            {
                break;
            }
            offset = next;
            moved += step;
        }

        if (endpoint == RangeEndpoint.Start)
        {
            _start = offset;
            if (_start > _end)
            {
                _end = _start;
            }
        }
        else
        {
            _end = offset;
            if (_end < _start)
            {
                _start = _end;
            }
        }
        return moved;
    }

    public void ExpandToUnit(TextUnit unit)
    {
        switch (Degrade(unit))
        {
            case TextUnit.Character:
                // A collapsed range expands forward to cover one character;
                // an existing span snaps outward to cluster boundaries.
                if (IsCollapsed)
                {
                    _end = _surface.NextCharacter(_start);
                }
                break;

            case TextUnit.Word:
                _start = _surface.WordStart(_start);
                _end = _surface.WordEnd(Math.Max(_start, _end == _start ? _start : _end - 1));
                break;

            case TextUnit.Line:
                _start = _surface.LineStart(_start);
                _end = _surface.LineEnd(_end == _start ? _start : _end - 1);
                if (_end < _start)
                {
                    _end = _start;
                }
                break;

            case TextUnit.Document:
                _start = 0;
                _end = _surface.Buffer.Length;
                break;
        }
        Normalize();
    }

    public void Collapse(bool toStart)
    {
        if (toStart)
        {
            _end = _start;
        }
        else
        {
            _start = _end;
        }
    }

    public void SetEndpoint(RangeEndpoint endpoint, ITextRange target, RangeEndpoint targetEndpoint)
    {
        if (target is not StringTextRange other || !ReferenceEquals(other._surface, _surface))
        {
            return;
        }
        var value = targetEndpoint == RangeEndpoint.Start ? other._start : other._end;
        if (endpoint == RangeEndpoint.Start)
        {
            _start = value;
            if (_start > _end)
            {
                _end = _start;
            }
        }
        else
        {
            _end = value;
            if (_end < _start)
            {
                _start = _end;
            }
        }
    }

    public int CompareEndpoints(RangeEndpoint endpoint, ITextRange other, RangeEndpoint otherEndpoint)
    {
        if (other is not StringTextRange o || !ReferenceEquals(o._surface, _surface))
        {
            return 0;
        }
        var mine = endpoint == RangeEndpoint.Start ? _start : _end;
        var theirs = otherEndpoint == RangeEndpoint.Start ? o._start : o._end;
        return mine.CompareTo(theirs);
    }

    // A plain string carries no formatting. Returning empty (rather than
    // throwing or returning null) keeps callers branch-free.
    public IReadOnlyDictionary<string, object?> GetAttributes()
        => EmptyAttributes;

    private static readonly IReadOnlyDictionary<string, object?> EmptyAttributes
        = new Dictionary<string, object?>(0);

    /// <summary>Map units this backend lacks onto the nearest one it has.</summary>
    private static TextUnit Degrade(TextUnit unit) => unit switch
    {
        TextUnit.Sentence or TextUnit.Paragraph => TextUnit.Line,
        TextUnit.Page => TextUnit.Document,
        _ => unit,
    };

    /// <summary>One unit step from <paramref name="offset"/>, or -1 at a boundary.</summary>
    private int StepOne(int offset, TextUnit unit, int direction) => unit switch
    {
        TextUnit.Character => direction > 0
            ? (offset >= _surface.Buffer.Length ? -1 : _surface.NextCharacter(offset))
            : (offset <= 0 ? -1 : _surface.PreviousCharacter(offset)),

        TextUnit.Word => direction > 0
            ? _surface.NextWordStart(offset)
            : _surface.PreviousWordStart(offset),

        TextUnit.Line => direction > 0
            ? _surface.NextLineStart(offset)
            : _surface.PreviousLineStart(offset),

        TextUnit.Document => direction > 0
            ? (offset >= _surface.Buffer.Length ? -1 : _surface.Buffer.Length)
            : (offset <= 0 ? -1 : 0),

        _ => -1,
    };

    private void Normalize()
    {
        var length = _surface.Buffer.Length;
        if (_start < 0)
        {
            _start = 0;
        }
        if (_end > length)
        {
            _end = length;
        }
        if (_start > length)
        {
            _start = length;
        }
        if (_end < _start)
        {
            _end = _start;
        }
    }
}
