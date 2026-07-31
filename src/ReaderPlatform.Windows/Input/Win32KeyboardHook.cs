using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using OpenReader.Abstractions.Input;
using OpenReader.Diagnostics;
using Serilog;

namespace OpenReader.Platform.Windows.Input;

/// <summary>
/// Low-level keyboard input source via <c>SetWindowsHookEx(WH_KEYBOARD_LL)</c>.
/// </summary>
/// <remarks>
/// The OS calls the hook on its own message-pump thread. We marshal each event
/// onto an unbounded channel so subscribers run off the hook thread; the hook
/// itself stays well under the 300 ms LowLevelHooksTimeout cliff.
///
/// <para><b>Modifier mapping.</b> Insert is the configured "Reader" modifier;
/// downstream gesture maps may rebind. CapsLock is also exposed as Reader so
/// users can pick either.</para>
///
/// <para><b>Pass-through.</b> This source observes only — it never returns
/// non-zero from the hook. Suppression of system keys belongs in the gesture
/// layer once command bindings are known.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class Win32KeyboardHook : IInputSource
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x00000010;

    /// <summary>Which physical key counts as the Reader modifier.</summary>
    public enum ReaderModifierKey
    {
        Insert = 0,
        CapsLock,
        Both,
    }

    private readonly Channel<RawInput> _events;
    private readonly ILogger _log;
    private readonly LowLevelKeyboardProc _hookProc;
    private readonly CancellationTokenSource _cts = new();
    private readonly ModifierStateTracker _modifiers = new();
    private readonly CapsLockTapDetector _capsLockTaps;

    private nint _hookHandle;
    private uint _hookThreadId;
    private Thread? _hookThread;
    private Task? _dispatchTask;
    private Func<RawInput, bool>? _suppressionPredicate;
    private Action? _onCapsLockSoloTap;
    private Action? _onTextInputKey;
    private bool _started;
    private bool _disposed;

    public Win32KeyboardHook()
    {
        _events = Channel.CreateUnbounded<RawInput>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
        _log = LoggerFactory.ForComponent("Input.KeyboardHook");
        _hookProc = HookCallback;
        _capsLockTaps = new CapsLockTapDetector(() =>
        {
            try
            {
                _onCapsLockSoloTap?.Invoke();
            }
            catch (Exception ex) when (!IsCritical(ex))
            {
                _log.Warning(ex, "CapsLock solo-tap handler threw");
            }
        });
    }

    public event EventHandler<RawInput>? RawInputReceived;

    /// <summary>
    /// Set a predicate that decides whether to swallow a key event before it
    /// reaches the foreground app. Returning <c>true</c> means "I consumed this
    /// — block the OS from dispatching it". Used for the Reader modifier
    /// (Insert / CapsLock) so it doesn't toggle overwrite mode or capslock,
    /// and for any chord that resolves to a bound command.
    /// </summary>
    /// <remarks>
    /// The predicate runs on the hook thread, synchronously. Keep it fast and
    /// allocation-free — the OS gives this hook ~300 ms before yanking it.
    /// </remarks>
    public void SetSuppressionPredicate(Func<RawInput, bool>? predicate)
    {
        _suppressionPredicate = predicate;
    }

    /// <summary>Choose which physical key acts as the Reader modifier.</summary>
    public void SetReaderModifierKey(ReaderModifierKey choice)
    {
        _modifiers.ReaderKey = choice;
    }

    /// <summary>
    /// Callback fired when CapsLock is pressed and released as a solo tap (no
    /// other key combined while held). The host uses this to drive the
    /// "two solo taps within ~450 ms toggle the screen reader" gesture without
    /// also triggering the OS CapsLock toggle. Always called on the hook thread.
    /// </summary>
    public void SetCapsLockSoloTapHandler(Action? handler)
    {
        _onCapsLockSoloTap = handler;
    }

    /// <summary>
    /// Set a callback fired <b>synchronously on the hook thread</b> for every
    /// text-producing key-down (letters, digits, punctuation, space, backspace,
    /// delete, numpad) — i.e. before the OS dispatches the keystroke to the
    /// focused app. The host wires this to <c>TypingState.NotifyTyping</c> so the
    /// speech pipeline can reliably mute the value re-read the keystroke
    /// triggers. Setting the flag here, rather than on the async dispatch loop,
    /// guarantees it is set before any UIA value-changed event the keystroke
    /// causes — eliminating the race that let the Run box machine-gun its
    /// growing value ("n", "no", "not", "note"...).
    /// </summary>
    /// <remarks>Runs inside the LL hook callback; keep it fast and allocation-free.</remarks>
    public void SetTextInputObserver(Action? observer)
    {
        _onTextInputKey = observer;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return ValueTask.CompletedTask;
        }
        _started = true;

        var ready = new ManualResetEventSlim(false);
        Exception? hookError = null;

        _hookThread = new Thread(() =>
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                using var module = process.MainModule;
                var moduleHandle = module is null ? GetModuleHandle(null) : GetModuleHandle(module.ModuleName);

                _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, moduleHandle, 0);
                if (_hookHandle == nint.Zero)
                {
                    hookError = new InvalidOperationException(
                        $"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
                    ready.Set();
                    return;
                }
                _hookThreadId = GetCurrentThreadId();
                _log.Information("Low-level keyboard hook installed (thread {ThreadId})", _hookThreadId);
                ready.Set();

                while (!_cts.IsCancellationRequested && GetMessage(out var msg, nint.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch (Exception ex) when (!IsCritical(ex))
            {
                hookError ??= ex;
                ready.Set();
            }
            finally
            {
                if (_hookHandle != nint.Zero)
                {
                    UnhookWindowsHookEx(_hookHandle);
                    _hookHandle = nint.Zero;
                }
            }
        })
        {
            IsBackground = true,
            Name = "OpenReader.KeyboardHook",
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();

        ready.Wait(cancellationToken);
        if (hookError is not null)
        {
            throw hookError;
        }

        _dispatchTask = Task.Run(() => DispatchLoopAsync(_cts.Token), _cts.Token);
        return ValueTask.CompletedTask;
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode != 0)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        try
        {
            var msg = (int)wParam;
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // Ignore synthetic events we generate ourselves to avoid feedback loops.
            if ((data.flags & LLKHF_INJECTED) != 0)
            {
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            var vk = (int)data.vkCode;
            var kind = msg switch
            {
                WM_KEYDOWN or WM_SYSKEYDOWN => InputEventKind.KeyDown,
                WM_KEYUP or WM_SYSKEYUP => InputEventKind.KeyUp,
                _ => (InputEventKind?)null,
            };
            if (kind is null)
            {
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            var down = kind == InputEventKind.KeyDown;
            _capsLockTaps.Observe(vk, down, capsIsActive: _modifiers.IsActiveReaderKey(ModifierStateTracker.VK_CAPITAL));
            _modifiers.Observe(vk, down);

            // Flag typing synchronously, before this keystroke reaches the app,
            // so the pipeline mutes the resulting value re-read. See
            // SetTextInputObserver for why this must run here, not on the
            // dispatch loop.
            if (down && IsTextInputKey(vk))
            {
                try
                {
                    _onTextInputKey?.Invoke();
                }
                catch (Exception ex) when (!IsCritical(ex))
                {
                    _log.Warning(ex, "text-input observer threw");
                }
            }

            // Normalize L/R modifier vk codes so chord bindings keyed on
            // VK_CONTROL / VK_SHIFT / VK_MENU match regardless of which
            // physical key was pressed.
            var raw = new RawInput(
                Source: InputSource.Keyboard,
                Kind: kind.Value,
                KeyCode: ModifierStateTracker.NormalizeKey(vk),
                Modifiers: _modifiers.Current,
                At: DateTimeOffset.UtcNow,
                Extras: null);

            _events.Writer.TryWrite(raw);

            var predicate = _suppressionPredicate;
            if (predicate is not null)
            {
                try
                {
                    if (predicate(raw))
                    {
                        return 1;
                    }
                }
                catch (Exception ex) when (!IsCritical(ex))
                {
                    _log.Warning(ex, "suppression predicate threw");
                }
            }
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Warning(ex, "ignored exception in keyboard hook");
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var ev in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    RawInputReceived?.Invoke(this, ev);
                }
                catch (Exception ex) when (!IsCritical(ex))
                {
                    _log.Warning(ex, "subscriber threw on raw input vk=0x{Vk:X2}", ev.KeyCode);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _events.Writer.TryComplete();
        await _cts.CancelAsync().ConfigureAwait(false);

        if (_hookThreadId != 0)
        {
            PostThreadMessage(_hookThreadId, 0x0012 /* WM_QUIT */, nint.Zero, nint.Zero);
        }

        if (_dispatchTask is not null)
        {
            try
            {
                await _dispatchTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }

        if (_hookThread is not null && _hookThread.IsAlive)
        {
            _hookThread.Join(TimeSpan.FromSeconds(2));
        }

        _cts.Dispose();
    }

    private static bool IsCritical(Exception ex)
        => ex is OutOfMemoryException or StackOverflowException or ThreadAbortException;

    /// <summary>
    /// True for vk codes that edit text content (letters, digits, punctuation,
    /// space, backspace, delete, numpad). Deliberately excludes arrows / Home /
    /// End / Tab / Enter / Esc / function keys / modifiers — navigation must not
    /// count as "typing" (so combo-box value changes from arrowing still
    /// announce, and Tab/Enter focus moves are not mistaken for edits).
    /// </summary>
    private static bool IsTextInputKey(int vk) =>
        vk is 0x08    // VK_BACK
            or 0x20   // VK_SPACE
            or 0x2E   // VK_DELETE
        || (vk >= 0x30 && vk <= 0x39)   // 0-9
        || (vk >= 0x41 && vk <= 0x5A)   // A-Z
        || (vk >= 0x60 && vk <= 0x6F)   // numpad 0-9 + operators
        || (vk >= 0xBA && vk <= 0xC0)   // OEM ; = , - . / `
        || (vk >= 0xDB && vk <= 0xDF);  // OEM [ \ ] ' and misc

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
