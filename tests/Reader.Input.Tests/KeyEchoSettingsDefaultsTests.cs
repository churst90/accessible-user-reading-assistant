using FluentAssertions;
using OpenReader.Input.Echo;
using Xunit;

namespace OpenReader.Input.Tests;

/// <summary>
/// Asserts that key echo is off by default. Users opt in to each echo via
/// Settings → Keyboard; flipping any of these to true by default would
/// regress the "no chatter while I type" baseline.
/// </summary>
public class KeyEchoSettingsDefaultsTests
{
    [Fact]
    public void Noisy_echoes_default_off()
    {
        var defaults = KeyEchoSettings.Defaults;

        defaults.SpeakCommandKeys.Should().BeFalse();
        defaults.SpeakCharacters.Should().BeFalse();
    }

    [Fact]
    public void Word_echo_defaults_on()
    {
        // Required for Notepad / search boxes to produce typing feedback
        // without per-character chatter.
        KeyEchoSettings.Defaults.SpeakWords.Should().BeTrue();
    }
}
