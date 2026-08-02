using System.Runtime.Versioning;
using Aura.Abstractions.Input;

namespace Aura.Platform.Windows.Input;

/// <summary>
/// Tracks the active <see cref="InputModifiers"/> mask as raw key-down/key-up
/// events arrive from a low-level hook. Pure state machine — no Win32 calls.
/// </summary>
/// <remarks>
/// <para>
/// Translates left/right modifier vk codes (<c>VK_LSHIFT</c> / <c>VK_LCONTROL</c>
/// etc.) into the corresponding <see cref="InputModifiers"/> flag, and an
/// active "Reader" key (Insert and/or CapsLock depending on choice) into
/// <see cref="InputModifiers.Reader"/>.
/// </para>
/// <para>
/// Also exposes a <see cref="NormalizeKey"/> helper that maps L/R modifier vk
/// codes to their generic forms so chord bindings keyed on <c>VK_CONTROL</c>
/// match regardless of which physical key the user pressed.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class ModifierStateTracker
{
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU = 0xA4;
    public const int VK_RMENU = 0xA5;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_INSERT = 0x2D;
    public const int VK_CAPITAL = 0x14;

    private InputModifiers _mask;
    private Win32KeyboardHook.ReaderModifierKey _readerKey = Win32KeyboardHook.ReaderModifierKey.Insert;

    public InputModifiers Current => _mask;

    public Win32KeyboardHook.ReaderModifierKey ReaderKey
    {
        get => _readerKey;
        set => _readerKey = value;
    }

    /// <summary>True if <paramref name="vk"/> is the active Reader-modifier key.</summary>
    public bool IsActiveReaderKey(int vk) => _readerKey switch
    {
        Win32KeyboardHook.ReaderModifierKey.Insert => vk == VK_INSERT,
        Win32KeyboardHook.ReaderModifierKey.CapsLock => vk == VK_CAPITAL,
        _ => vk == VK_INSERT || vk == VK_CAPITAL,
    };

    /// <summary>Update the modifier mask in response to a key event.</summary>
    public void Observe(int vk, bool down)
    {
        var flag = vk switch
        {
            VK_LSHIFT or VK_RSHIFT => InputModifiers.Shift,
            VK_LCONTROL or VK_RCONTROL => InputModifiers.Control,
            VK_LMENU or VK_RMENU => InputModifiers.Alt,
            VK_LWIN or VK_RWIN => InputModifiers.Win,
            _ => InputModifiers.None,
        };
        if (flag == InputModifiers.None && IsActiveReaderKey(vk))
        {
            flag = InputModifiers.Reader;
        }
        if (flag == InputModifiers.None)
        {
            return;
        }
        _mask = down ? _mask | flag : _mask & ~flag;
    }

    /// <summary>
    /// Map a left/right modifier vk code to its generic form (VK_SHIFT,
    /// VK_CONTROL, VK_MENU). Non-modifier keys pass through unchanged.
    /// </summary>
    public static int NormalizeKey(int vk) => vk switch
    {
        VK_LSHIFT or VK_RSHIFT => VK_SHIFT,
        VK_LCONTROL or VK_RCONTROL => VK_CONTROL,
        VK_LMENU or VK_RMENU => VK_MENU,
        _ => vk,
    };
}
