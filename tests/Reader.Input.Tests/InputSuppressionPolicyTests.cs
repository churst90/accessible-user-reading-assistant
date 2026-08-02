using FluentAssertions;
using Aura.Abstractions.Input;
using Aura.Input.Commands;
using Aura.Input.Gestures;
using Xunit;

namespace Aura.Input.Tests;

public class InputSuppressionPolicyTests
{
    private static RawInput Down(int vk, InputModifiers mods = InputModifiers.None)
        => new(InputSource.Keyboard, InputEventKind.KeyDown, vk, mods, DateTimeOffset.UtcNow);

    private static RawInput Up(int vk, InputModifiers mods = InputModifiers.None)
        => new(InputSource.Keyboard, InputEventKind.KeyUp, vk, mods, DateTimeOffset.UtcNow);

    [Fact]
    public void Desktop_layout_suppresses_Insert_only()
    {
        var map = new GestureMap();
        var policy = new InputSuppressionPolicy(map, KeyboardLayout.Desktop);

        policy.ShouldSuppress(Down(0x2D, InputModifiers.Reader)).Should().BeTrue();
        policy.ShouldSuppress(Up(0x2D, InputModifiers.None)).Should().BeTrue();

        // CapsLock passes through in desktop layout — user can still toggle it normally.
        policy.ShouldSuppress(Down(0x14, InputModifiers.None)).Should().BeFalse();
    }

    [Fact]
    public void Laptop_layout_suppresses_CapsLock_only()
    {
        var map = new GestureMap();
        var policy = new InputSuppressionPolicy(map, KeyboardLayout.Laptop);

        policy.ShouldSuppress(Down(0x14, InputModifiers.Reader)).Should().BeTrue();
        policy.ShouldSuppress(Up(0x14, InputModifiers.None)).Should().BeTrue();

        // Insert passes through in laptop layout — Insert can toggle overwrite normally.
        policy.ShouldSuppress(Down(0x2D, InputModifiers.None)).Should().BeFalse();
    }

    [Fact]
    public void Bound_chord_is_suppressed_on_KeyDown()
    {
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map);
        var policy = new InputSuppressionPolicy(map);

        // Reader+Down → ReadNextLine
        policy.ShouldSuppress(Down(0x28, InputModifiers.Reader)).Should().BeTrue();
    }

    [Fact]
    public void Numpad_keys_are_suppressed_in_desktop_layout()
    {
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Desktop);
        var policy = new InputSuppressionPolicy(map, KeyboardLayout.Desktop);

        // VK_NUMPAD4 → ReadPreviousCharacter; must be swallowed so foreground doesn't see "4".
        policy.ShouldSuppress(Down(0x64, InputModifiers.None)).Should().BeTrue();
    }

    [Fact]
    public void Unbound_key_is_not_suppressed()
    {
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map);
        var policy = new InputSuppressionPolicy(map);

        // Plain 'A' with no modifiers — typing.
        policy.ShouldSuppress(Down(0x41, InputModifiers.None)).Should().BeFalse();
    }

    [Fact]
    public void Bare_Ctrl_StopSpeech_is_observed_not_suppressed()
    {
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map);
        var policy = new InputSuppressionPolicy(map);

        // Ctrl alone is bound to StopSpeech but must pass through to the foreground.
        policy.ShouldSuppress(Down(0x11, InputModifiers.Control)).Should().BeFalse();
    }

    [Fact]
    public void KeyUp_for_non_modifier_is_not_suppressed()
    {
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map);
        var policy = new InputSuppressionPolicy(map);

        policy.ShouldSuppress(Up(0x28, InputModifiers.Reader)).Should().BeFalse();
    }

    [Fact]
    public void Bound_chord_with_only_OS_modifiers_is_suppressed()
    {
        // E.g., a hypothetical Ctrl+Shift+P binding the user added — must take
        // precedence over the foreground.
        var map = new GestureMap();
        map.Bind(new KeyChord(0x50 /* P */, InputModifiers.Control | InputModifiers.Shift), ReaderCommand.SayAll);
        var policy = new InputSuppressionPolicy(map);

        policy.ShouldSuppress(Down(0x50, InputModifiers.Control | InputModifiers.Shift)).Should().BeTrue();
    }
}
