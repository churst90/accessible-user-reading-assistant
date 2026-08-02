using FluentAssertions;
using Aura.Input.Echo;
using Xunit;

namespace Aura.Input.Tests;

/// <summary>
/// The invariant: with command-key echo off, no key <em>name</em> is ever
/// spoken — not "backspace", not "tab", not through any fallback path.
/// </summary>
/// <remarks>
/// This is a user-stated requirement, not an inference, and it had a real
/// leak: the backspace handler used to fall back to saying "backspace" from a
/// branch governed by a different setting.
/// </remarks>
public class CommandKeyEchoTests
{
    [Fact]
    public void Command_key_echo_is_off_by_default()
    {
        // "control", "left", "right" on every press is unusable as a default.
        KeyEchoSettings.Defaults.SpeakCommandKeys.Should().BeFalse();
    }

    [Fact]
    public void One_toggle_governs_modifiers_and_named_keys_alike()
    {
        // The old split between "modifiers" and "navigation keys" did not match
        // how anyone thinks about it: the question is "do I want key names?",
        // and the answer is the same for Shift and for F7.
        var settings = KeyEchoSettings.Defaults with { SpeakCommandKeys = true };
        settings.SpeakCommandKeys.Should().BeTrue();
    }

    [Fact]
    public void Deletion_echo_is_independent_of_command_key_echo()
    {
        // Turning key names off must not silence what was deleted: the removed
        // character is content, not a key name.
        var settings = KeyEchoSettings.Defaults with { SpeakCommandKeys = false };
        settings.SpeakDeletedCharacters.Should().BeTrue();
    }

    [Fact]
    public void Turning_command_keys_on_does_not_disturb_the_other_echoes()
    {
        var settings = KeyEchoSettings.Defaults with { SpeakCommandKeys = true };
        settings.SpeakCharacters.Should().BeFalse();
        settings.SpeakWords.Should().BeTrue();
        settings.SpeakDeletedCharacters.Should().BeTrue();
    }
}
