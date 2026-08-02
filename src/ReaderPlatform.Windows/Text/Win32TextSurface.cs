using System.Runtime.Versioning;
using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Text;
using Aura.Platform.Windows.Interop;

namespace Aura.Platform.Windows.Text;

/// <summary>
/// <see cref="ITextSurface"/> over a classic Win32 edit control, via
/// <c>WM_GETTEXT</c> and <c>EM_GETSEL</c>.
/// </summary>
/// <remarks>
/// <para>
/// The fallback for controls that expose no <c>TextPattern</c> — classic
/// Notepad's edit, legacy Win32 textboxes, and a long tail of MFC and Delphi
/// applications that only reach UIA through the MSAA proxy.
/// </para>
/// <para>
/// It does almost nothing itself. Two messages give the whole buffer and the
/// caret offset, which is exactly what <see cref="StringTextSurface"/> already
/// takes — so all the unit arithmetic comes from the one tested
/// implementation instead of being re-derived here in
/// <c>EM_LINEFROMCHAR</c> / <c>EM_LINEINDEX</c> / <c>EM_GETLINE</c> terms.
/// That deleted roughly 120 lines of hand-rolled column and word-boundary
/// arithmetic from the old caret tracker, along with its 16-bit
/// <c>EM_GETSEL</c> truncation bug.
/// </para>
/// <para>
/// <b>One buffer instance, refreshed in place.</b> Ranges are only comparable
/// against ranges from the same surface, and caret tracking works by comparing
/// the previous position with the current one — so re-snapshotting into a
/// fresh <see cref="StringTextSurface"/> each call would silently make every
/// comparison return "equal" and caret following would go quiet. Refreshing
/// the existing buffer keeps previously handed-out ranges comparable, with
/// their offsets pointing where they were.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class Win32TextSurface : ITextSurface
{
    private readonly nint _hwnd;
    private readonly StringTextSurface _buffer;

    internal Win32TextSurface(nint hwnd, NodeId nodeId)
    {
        _hwnd = hwnd;
        NodeId = nodeId;
        _buffer = new StringTextSurface(string.Empty, 0, nodeId);
    }

    public NodeId NodeId { get; }

    public bool SupportsUnit(TextUnit unit) => _buffer.SupportsUnit(unit);

    public ITextRange? GetCaret() => Refresh() ? _buffer.GetCaret() : null;

    public ITextRange? GetSelection() => Refresh() ? _buffer.GetSelection() : null;

    public ITextRange GetDocumentRange()
        => Refresh() ? _buffer.GetDocumentRange() : EmptyTextRange.Instance;

    /// <summary>
    /// Pull the control's current text and selection into the buffer. Returns
    /// false when the window did not answer within the timeout — a hung app
    /// yields no reading rather than a wedged thread.
    /// </summary>
    private bool Refresh()
    {
        if (!Win32Text.TrySnapshotWithSelection(_hwnd, out var text, out var start, out var end))
        {
            return false;
        }
        _buffer.Text = text;
        // Anchor at the selection start, caret at the end: for a collapsed
        // selection both land on the caret, and for a live selection this is
        // the forward-drag orientation the user almost always has.
        _buffer.Select(start, end);
        return true;
    }
}
