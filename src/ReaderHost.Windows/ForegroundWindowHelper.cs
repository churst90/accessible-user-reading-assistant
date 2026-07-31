using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;

namespace OpenReader.Host;

/// <summary>
/// Forces a WPF <see cref="Window"/> to the foreground after <c>Show()</c>.
/// </summary>
/// <remarks>
/// When a window is opened from a non-foreground thread or process — which is
/// our case (the host's UI dispatcher runs on a dedicated STA thread that
/// never owns the foreground), Windows refuses to steal focus and the dialog
/// appears behind whatever the user was using. The user then has to
/// alt+shift+tab to reach it.
///
/// <para>The Topmost-bounce trick is the most reliable workaround that doesn't
/// require <c>SetForegroundWindow</c> permissions. We briefly mark the window
/// topmost (which Windows allows even without focus rights), call
/// <see cref="Window.Activate"/>, then drop topmost so the user can move other
/// windows over it normally.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class ForegroundWindowHelper
{
    public static void BringToFront(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // ContentRendered is the latest stable hook — it fires after Loaded
        // and after the first frame, when the window is genuinely on screen
        // and Activate/SetForegroundWindow have a chance of being honored.
        // Fire once.
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            window.ContentRendered -= handler;
            Apply(window);
        };
        window.ContentRendered += handler;

        // Some windows are already rendered by the time we get here (re-show).
        if (window.IsLoaded)
        {
            Apply(window);
        }
    }

    private static void Apply(Window window)
    {
        try
        {
            // Allow our process to grab focus (the foreground process must
            // grant us this; pass ASFW_ANY = -1 which works for foreground
            // hooks scenarios — best-effort only).
            AllowSetForegroundWindow(-1);

            // Topmost-bounce: WPF allows Topmost without focus rights, and
            // setting it briefly raises the window above the foreground app
            // before we try to claim activation properly.
            window.Topmost = true;
            window.Activate();

            var handle = new WindowInteropHelper(window).Handle;
            if (handle != nint.Zero)
            {
                // Combine the input thread with the foreground thread for the
                // duration of the call. That defeats the foreground-lock
                // policy in cases where AllowSetForegroundWindow alone is
                // insufficient.
                AttachAndForeground(handle);
            }

            window.Topmost = false;
            window.Focus();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Best-effort; if any of this fails the user can alt+tab to the
            // dialog. Don't take the host down.
            _ = ex;
        }
    }

    private static void AttachAndForeground(nint targetHandle)
    {
        var foreground = GetForegroundWindow();
        if (foreground == nint.Zero || foreground == targetHandle)
        {
            SetForegroundWindow(targetHandle);
            return;
        }

        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var ownThread = GetCurrentThreadId();
        var attached = false;
        try
        {
            if (foregroundThread != ownThread)
            {
                attached = AttachThreadInput(ownThread, foregroundThread, attach: true);
            }
            SetForegroundWindow(targetHandle);
            BringWindowToTop(targetHandle);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(ownThread, foregroundThread, attach: false);
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

}
