using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Text;

namespace OpenReader.Core.Review;

/// <summary>
/// The review position — a movable point in the focused control's text that
/// the user drives independently of the system caret.
/// </summary>
/// <remarks>
/// <para>
/// Now a thin policy layer over <see cref="ITextRange"/>. It used to hold a
/// <c>string</c> snapshot plus an <c>int</c> offset and re-derive word and
/// line boundaries itself, which meant three problems that are gone by
/// construction rather than by fixing:
/// </para>
/// <list type="bullet">
///   <item>The snapshot went stale the moment the user typed, so review read
///   deleted content until the next focus change. <c>Refresh</c> existed to
///   paper over it. A range reads through to the control, so there is nothing
///   to go stale.</item>
///   <item>Word and line boundaries were computed here, differently from how
///   the caret tracker computed them, so review and caret-follow could
///   disagree about where a word ended. Both now ask the same surface.</item>
///   <item>Review could not follow the caret — roadmap 3.6 #3 — because the
///   two were different kinds of thing. <see cref="FollowCaret"/> is now a
///   one-line operation because they are the same type over the same
///   surface.</item>
/// </list>
/// <para>
/// The public method names are unchanged so the command bindings did not have
/// to move with it.
/// </para>
/// </remarks>
public sealed class ReviewCursor
{
    /// <summary>
    /// Cap on any single read. A minified file can be one line of several
    /// megabytes; one keystroke must not turn into that much speech.
    /// </summary>
    private const int ReadCap = 8192;

    private readonly ITextSurfaceProvider _surfaces;
    private readonly object _gate = new();
    private ITextSurface? _surface;
    private ITextRange? _cursor;
    private NodeId _nodeId;

    public ReviewCursor(ITextSurfaceProvider surfaces)
    {
        _surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
    }

    /// <summary>True when the cursor is bound to a surface that has any text.</summary>
    public bool HasText
    {
        get
        {
            lock (_gate)
            {
                return _surface is not null && _surface.GetDocumentRange().GetText(1).Length > 0;
            }
        }
    }

    /// <summary>
    /// Bind to a node. Re-resolving is cheap — the provider caches surfaces per
    /// node — so calling this on every focus change is fine.
    /// </summary>
    public void SyncTo(AccessibleNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        lock (_gate)
        {
            if (_surface is not null && _nodeId == node.Id)
            {
                return;
            }
            Bind(node);
        }
    }

    /// <summary>
    /// Re-bind to the node's current surface.
    /// </summary>
    /// <remarks>
    /// Kept for API compatibility. It is very nearly a no-op now: ranges read
    /// through to the live control, so there is no cached snapshot to refresh.
    /// The old implementation needed this on every value-changed event or
    /// review would read text the user had already deleted.
    /// </remarks>
    public void Refresh(AccessibleNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        lock (_gate)
        {
            if (_nodeId == node.Id && _surface is not null)
            {
                return;
            }
            Bind(node);
        }
    }

    /// <summary>
    /// Snap the review position to the system caret.
    /// </summary>
    /// <remarks>
    /// This is roadmap 3.6 #3 — "review cursor doesn't track the system caret".
    /// It stopped being a feature to build the moment review and the caret
    /// became the same type over the same surface.
    /// </remarks>
    public bool FollowCaret()
    {
        lock (_gate)
        {
            var caret = _surface?.GetCaret();
            if (caret is null)
            {
                return false;
            }
            _cursor = caret.Clone();
            _cursor.Collapse(toStart: true);
            return true;
        }
    }

    public void MoveToStart()
    {
        lock (_gate)
        {
            if (_surface is null)
            {
                return;
            }
            _cursor = _surface.GetDocumentRange();
            _cursor.Collapse(toStart: true);
        }
    }

    public void MoveToEnd()
    {
        lock (_gate)
        {
            if (_surface is null)
            {
                return;
            }
            _cursor = _surface.GetDocumentRange();
            _cursor.Collapse(toStart: false);
        }
    }

    public string ReadCurrentCharacter() => ReadUnit(TextUnit.Character);

    public string ReadCurrentWord() => ReadUnit(TextUnit.Word);

    public string ReadCurrentLine() => ReadUnit(TextUnit.Line);

    public string MoveNextCharacter() => MoveBy(TextUnit.Character, 1);

    public string MovePreviousCharacter() => MoveBy(TextUnit.Character, -1);

    public string MoveNextWord() => MoveBy(TextUnit.Word, 1);

    public string MovePreviousWord() => MoveBy(TextUnit.Word, -1);

    public string MoveNextLine() => MoveBy(TextUnit.Line, 1);

    public string MovePreviousLine() => MoveBy(TextUnit.Line, -1);

    private void Bind(AccessibleNode node)
    {
        _nodeId = node.Id;
        _surface = _surfaces.GetSurface(node);
        if (_surface is null)
        {
            _cursor = null;
            return;
        }
        // Start where the user is, not at the top. A review cursor that always
        // resets to offset zero makes the user re-navigate to the place they
        // were already looking at.
        var caret = _surface.GetCaret();
        _cursor = caret?.Clone() ?? _surface.GetDocumentRange();
        _cursor.Collapse(toStart: true);
    }

    private string ReadUnit(TextUnit unit)
    {
        lock (_gate)
        {
            if (_cursor is null)
            {
                return string.Empty;
            }
            var probe = _cursor.Clone();
            probe.Collapse(toStart: true);
            probe.ExpandToUnit(unit);
            return probe.GetText(ReadCap);
        }
    }

    /// <summary>
    /// Step one unit and read what we landed on. Returns empty at a document
    /// boundary — the caller announces nothing, which is how the user hears
    /// that they have reached the end.
    /// </summary>
    private string MoveBy(TextUnit unit, int direction)
    {
        lock (_gate)
        {
            if (_cursor is null || _surface is null)
            {
                return string.Empty;
            }
            var probe = _cursor.Clone();
            if (probe.Move(unit, direction) == 0)
            {
                return string.Empty;
            }

            var text = probe.GetText(ReadCap);

            // Stepping forward off the last character lands on the document's
            // end position, which reads as empty. Don't strand the cursor
            // there — the user would then have to press "previous" twice to
            // get back to the last character.
            //
            // Only when it is genuinely the end, though: an empty reading in
            // the middle of a document is a blank line, and blank lines must
            // stay traversable.
            if (text.Length == 0 && direction > 0 && IsAtDocumentEnd(probe))
            {
                return string.Empty;
            }

            _cursor = probe.Clone();
            _cursor.Collapse(toStart: true);
            return text;
        }
    }

    private bool IsAtDocumentEnd(ITextRange range)
    {
        if (_surface is null)
        {
            return false;
        }
        var document = _surface.GetDocumentRange();
        return range.CompareEndpoints(RangeEndpoint.Start, document, RangeEndpoint.End) >= 0;
    }
}
