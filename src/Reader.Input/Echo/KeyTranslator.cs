using System.Runtime.InteropServices;
using Aura.Abstractions.Input;

namespace Aura.Input.Echo;

/// <summary>
/// Translates a virtual-key code into the printable character it produces
/// under the current keyboard layout, taking Shift / CapsLock into account.
/// </summary>
/// <remarks>
/// Wraps Win32 <c>ToUnicodeEx</c>. Returns null for non-character keys
/// (modifiers, function keys, navigation keys). The dead-key state is reset
/// on every call so we don't accidentally swallow the next press.
/// </remarks>
public static class KeyTranslator
{
    private const int VK_CAPITAL = 0x14;

    /// <summary>
    /// Try to translate a key event to its produced character. Reads the OS
    /// CapsLock toggle state via <c>GetKeyState</c>; if you need a stable
    /// result for tests pass an explicit override via
    /// <see cref="ToCharacter(int, InputModifiers, bool?)"/>.
    /// </summary>
    public static char? ToCharacter(int virtualKey, InputModifiers modifiers)
        => ToCharacter(virtualKey, modifiers, capsLockOn: null);

    /// <summary>
    /// Translate a key event with an explicit CapsLock toggle state. Pass
    /// <c>null</c> to read the OS state at call time.
    /// </summary>
    public static char? ToCharacter(int virtualKey, InputModifiers modifiers, bool? capsLockOn)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        var caps = capsLockOn ?? IsCapsLockOn();
        return TryTranslate(virtualKey, modifiers, caps);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsCapsLockOn()
    {
        // GetKeyState returns the *toggle* bit (low bit) for VK_CAPITAL —
        // independent of whether the key is currently pressed. We only care
        // about the toggle.
        return (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static unsafe char? TryTranslate(int virtualKey, InputModifiers modifiers, bool capsLockOn)
    {
        var keyboardState = new byte[256];
        if ((modifiers & InputModifiers.Shift) != 0)
        {
            keyboardState[0x10] = 0x80;
        }
        if ((modifiers & InputModifiers.Control) != 0)
        {
            keyboardState[0x11] = 0x80;
        }
        if ((modifiers & InputModifiers.Alt) != 0)
        {
            keyboardState[0x12] = 0x80;
        }
        if (capsLockOn)
        {
            // ToUnicodeEx looks at the low bit of the keyboard state for VK_CAPITAL
            // to decide whether CapsLock is active. The high bit means "currently
            // pressed" — only the low bit matters here.
            keyboardState[VK_CAPITAL] = 0x01;
        }

        var layout = GetKeyboardLayout(0);
        var scan = MapVirtualKeyEx((uint)virtualKey, 0 /* MAPVK_VK_TO_VSC */, layout);
        var buffer = stackalloc char[8];
        var rc = ToUnicodeEx((uint)virtualKey, scan, keyboardState, buffer, 8, 0, layout);
        if (rc == 1)
        {
            var ch = buffer[0];
            return char.IsControl(ch) ? null : ch;
        }
        return null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern unsafe int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        char* pwszBuff, int cchBuff, uint wFlags, nint dwhkl);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, nint dwhkl);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
