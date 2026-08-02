using FluentAssertions;
using OpenReader.Config;
using Xunit;

namespace OpenReader.Config.Tests;

/// <summary>
/// Locks down the "all key echo defaults off" decision so a future tweak
/// doesn't silently re-enable noisy announcements that users opt into.
/// </summary>
public class KeyboardConfigDefaultsTests
{
    [Fact]
    public void Modifier_navigation_and_character_echo_default_off()
    {
        var defaults = KeyboardConfig.Defaults();

        defaults.SpeakCommandKeys.Should().Be(false);
        defaults.SpeakCharacters.Should().Be(false);
    }

    [Fact]
    public void Word_echo_defaults_on()
    {
        // Word echo is the cheap audible feedback for typing in Notepad,
        // search boxes, etc. It's much less noisy than per-character echo.
        KeyboardConfig.Defaults().SpeakWords.Should().Be(true);
    }

    [Fact]
    public void Layout_defaults_to_desktop()
    {
        KeyboardConfig.Defaults().Layout.Should().Be("desktop");
    }

    [Fact]
    public void Reader_modifier_defaults_to_both()
    {
        InputConfig.Defaults().ReaderModifier.Should().Be("both");
    }
}
