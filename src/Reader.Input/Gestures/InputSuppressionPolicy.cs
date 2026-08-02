using Aura.Abstractions.Input;
using Aura.Input.Commands;

namespace Aura.Input.Gestures;

/// <summary>
/// Decides whether a raw key event should be swallowed before reaching the
/// foreground app.
/// </summary>
/// <remarks>
/// <para>Reasons to swallow:</para>
/// <list type="number">
/// <item>The chord resolves to a bound <see cref="ReaderCommand"/>. Without
/// suppression Reader+Down would also scroll Notepad.</item>
/// <item>The key is the active Reader modifier (Insert in desktop layout,
/// CapsLock in laptop). Both have OS side effects (overwrite mode, capslock)
/// that we never want to trigger when used as a modifier.</item>
/// </list>
/// <para>Bare OS modifiers (Shift / Ctrl / Alt / Win) are never swallowed.
/// <see cref="ReaderCommand.StopSpeech"/> on bare Ctrl is observed-only.</para>
/// </remarks>
public sealed class InputSuppressionPolicy
{
    private const int VK_INSERT = 0x2D;
    private const int VK_CAPITAL = 0x14;

    private readonly GestureMap _map;
    private KeyboardLayout _layout;

    public InputSuppressionPolicy(GestureMap map, KeyboardLayout layout = KeyboardLayout.Desktop)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _layout = layout;
    }

    /// <summary>Hot-swap the active layout; affects which modifier key gets swallowed.</summary>
    public void SetLayout(KeyboardLayout layout) => _layout = layout;

    /// <summary>True iff this raw event should be blocked from the foreground app.</summary>
    public bool ShouldSuppress(RawInput input)
    {
        if (input.Kind != InputEventKind.KeyDown && input.Kind != InputEventKind.KeyUp)
        {
            return false;
        }

        if (IsActiveReaderModifierKey(input.KeyCode))
        {
            return true;
        }

        if (input.Kind != InputEventKind.KeyDown)
        {
            return false;
        }

        var command = _map.Resolve(input);
        if (command == ReaderCommand.None)
        {
            return false;
        }

        // StopSpeech bound to a bare OS modifier (e.g. plain Ctrl) is observation-only.
        if (command == ReaderCommand.StopSpeech && IsBareOsModifier(input.KeyCode))
        {
            return false;
        }

        return true;
    }

    private bool IsActiveReaderModifierKey(int keyCode) => _layout switch
    {
        KeyboardLayout.Laptop => keyCode == VK_CAPITAL,
        _ => keyCode == VK_INSERT,
    };

    private static bool IsBareOsModifier(int keyCode)
        => keyCode is 0x10 /* VK_SHIFT */ or 0x11 /* VK_CONTROL */ or 0x12 /* VK_MENU */
            or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5
            or 0x5B or 0x5C;
}
