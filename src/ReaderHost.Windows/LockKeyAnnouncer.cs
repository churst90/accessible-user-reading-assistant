using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenReader.Abstractions.Input;

namespace OpenReader.Host;

/// <summary>
/// Announces toggle-state changes for CapsLock, NumLock, and ScrollLock by
/// reading <c>GetKeyState</c> after each key-down event. Always on — these are
/// state changes the user almost always wants to hear.
/// </summary>
/// <remarks>
/// Lives outside <c>KeyEchoService</c> because the toggle-key announcements
/// are not opt-in — users routinely complain when their CapsLock state is
/// silently flipped. They sit at a different policy level than the optional
/// "speak every navigation key" echo.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class LockKeyAnnouncer : IDisposable
{
    private const int VK_CAPITAL = 0x14;
    private const int VK_NUMLOCK = 0x90;
    private const int VK_SCROLL = 0x91;

    private readonly IInputSource _source;
    private readonly Action<string> _speak;
    private readonly Func<bool> _capsLockIsReaderModifier;
    private bool _started;
    private bool _disposed;

    public LockKeyAnnouncer(IInputSource source, Action<string> speak, Func<bool> capsLockIsReaderModifier)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _speak = speak ?? throw new ArgumentNullException(nameof(speak));
        _capsLockIsReaderModifier = capsLockIsReaderModifier ?? throw new ArgumentNullException(nameof(capsLockIsReaderModifier));
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        _source.RawInputReceived += OnRawInput;
    }

    private void OnRawInput(object? sender, RawInput input)
    {
        if (input.Kind != InputEventKind.KeyDown)
        {
            return;
        }
        switch (input.KeyCode)
        {
            case VK_CAPITAL:
                // In laptop layout CapsLock is the Reader modifier — its toggle
                // is suppressed, so there's nothing to announce.
                if (_capsLockIsReaderModifier())
                {
                    return;
                }
                Announce("caps lock", VK_CAPITAL);
                break;
            case VK_NUMLOCK:
                Announce("num lock", VK_NUMLOCK);
                break;
            case VK_SCROLL:
                Announce("scroll lock", VK_SCROLL);
                break;
        }
    }

    private void Announce(string label, int vk)
    {
        // GetKeyState returns 0x0001 in the low bit when the toggle is on. The
        // hook callback fires before the OS finishes processing the keydown, so
        // by the time this runs on the dispatch thread, the toggle has flipped.
        var state = GetKeyState(vk);
        var on = (state & 1) != 0;
        _speak(on ? $"{label} on" : $"{label} off");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _source.RawInputReceived -= OnRawInput;
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
