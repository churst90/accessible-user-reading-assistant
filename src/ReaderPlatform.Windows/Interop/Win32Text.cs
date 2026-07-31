using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenReader.Platform.Windows.Interop;

/// <summary>
/// Hang-safe reads of a window's text and caret position.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every call here goes through <c>SendMessageTimeout</c>, never
/// <c>SendMessage</c>.</b> A plain <c>SendMessage</c> blocks until the target
/// window's message loop processes it — and if that loop is not pumping
/// (mid-save, a GC pause, a native modal, or a genuinely crashed app) it never
/// returns. On the reader's hot path that means a thread-pool worker per
/// keystroke wedged forever, and shortly after that a silent screen reader
/// with no way for the user to know why. Going quiet is the worst failure mode
/// this program has; a stale read is always better.
/// </para>
/// <para>
/// <c>SMTO_ABORTIFHUNG</c> returns immediately if the window is already known
/// to be hung, so the common case does not even wait out the timeout.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class Win32Text
{
    /// <summary>
    /// How long to wait for a window to answer. Above roughly 30 ms a user
    /// perceives lag, so a read that has not landed by then has already lost
    /// its value — better to abandon it than to hold the path open.
    /// </summary>
    internal const uint DefaultTimeoutMs = 100;

    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_GETTEXTLENGTH = 0x000E;
    private const uint EM_GETSEL = 0x00B0;

    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint SMTO_ERRORONEXIT = 0x0020;
    private const uint Flags = SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT;

    /// <summary>Upper bound on a single text read. A document larger than this is not being spoken whole anyway.</summary>
    internal const int MaxCharacters = 200_000;

    /// <summary>
    /// Read a window's full text. Returns false on timeout, on a hung window,
    /// or when the control has no text.
    /// </summary>
    internal static bool TryGetText(nint hwnd, out string text, uint timeoutMs = DefaultTimeoutMs)
    {
        text = string.Empty;
        if (hwnd == 0)
        {
            return false;
        }

        if (SendMessageTimeout(hwnd, WM_GETTEXTLENGTH, nint.Zero, nint.Zero, Flags, timeoutMs, out var rawLength) == 0)
        {
            return false;
        }

        var length = (int)rawLength;
        if (length <= 0)
        {
            return false;
        }
        if (length > MaxCharacters)
        {
            length = MaxCharacters;
        }

        // +1 for the terminating null WM_GETTEXT always writes.
        var buffer = new char[length + 1];
        if (SendMessageTimeoutBuffer(hwnd, WM_GETTEXT, (nint)(length + 1), buffer, Flags, timeoutMs, out var copied) == 0)
        {
            return false;
        }

        var count = Math.Clamp((int)copied, 0, length);
        if (count == 0)
        {
            return false;
        }
        text = new string(buffer, 0, count);
        return true;
    }

    /// <summary>
    /// Read the selection range of an edit control as character offsets.
    /// For a caret with no selection, start and end are equal.
    /// </summary>
    /// <remarks>
    /// Uses the pointer form of <c>EM_GETSEL</c>. The packed return-value form
    /// — <c>(int)(result &amp; 0xFFFF)</c> — silently truncates at 65,535
    /// characters, so in any document longer than that the caret offset is
    /// wrong and every derived line and word read with it is wrong too.
    /// </remarks>
    internal static bool TryGetSelection(nint hwnd, out int start, out int end, uint timeoutMs = DefaultTimeoutMs)
    {
        start = 0;
        end = 0;
        if (hwnd == 0)
        {
            return false;
        }

        var startBuf = Marshal.AllocHGlobal(sizeof(uint));
        var endBuf = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            Marshal.WriteInt32(startBuf, 0);
            Marshal.WriteInt32(endBuf, 0);

            if (SendMessageTimeout(hwnd, EM_GETSEL, startBuf, endBuf, Flags, timeoutMs, out _) == 0)
            {
                return false;
            }

            start = Marshal.ReadInt32(startBuf);
            end = Marshal.ReadInt32(endBuf);
            if (start < 0 || end < 0)
            {
                return false;
            }
            if (start > end)
            {
                (start, end) = (end, start);
            }
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(startBuf);
            Marshal.FreeHGlobal(endBuf);
        }
    }

    /// <summary>
    /// Read a control's text and caret offset together — everything needed to
    /// build a <c>StringTextSurface</c> over a classic Win32 edit.
    /// </summary>
    internal static bool TrySnapshot(nint hwnd, out string text, out int caretOffset, uint timeoutMs = DefaultTimeoutMs)
    {
        caretOffset = 0;
        if (!TryGetText(hwnd, out text, timeoutMs))
        {
            return false;
        }
        // A control can legitimately have text but refuse EM_GETSEL (it isn't
        // an edit). Keep the text and report the caret at the start.
        if (TryGetSelection(hwnd, out _, out var end, timeoutMs))
        {
            caretOffset = Math.Clamp(end, 0, text.Length);
        }
        return true;
    }

    /// <summary>
    /// Read a control's selection as offsets alongside its text. Returns false
    /// when the text could not be read at all.
    /// </summary>
    internal static bool TrySnapshotWithSelection(
        nint hwnd,
        out string text,
        out int selectionStart,
        out int selectionEnd,
        uint timeoutMs = DefaultTimeoutMs)
    {
        selectionStart = 0;
        selectionEnd = 0;
        if (!TryGetText(hwnd, out text, timeoutMs))
        {
            return false;
        }
        if (TryGetSelection(hwnd, out var s, out var e, timeoutMs))
        {
            selectionStart = Math.Clamp(s, 0, text.Length);
            selectionEnd = Math.Clamp(e, 0, text.Length);
        }
        return true;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint hWnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeoutBuffer(
        nint hWnd, uint msg, nint wParam, [Out] char[] lParam, uint flags, uint timeout, out nint result);
}
