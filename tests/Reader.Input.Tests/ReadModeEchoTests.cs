using Aura.Input.Echo;
using FluentAssertions;
using Xunit;

namespace Aura.Input.Tests;

/// <summary>
/// Character and word echo are independent checkboxes, and both may be on.
/// NVDA presents them as a four-way list (off / characters / words / both);
/// those four options are just the combinations of two booleans, so spelling
/// them out makes the user translate their intent into someone else's
/// enumeration.
/// </summary>
public class ReadModeEchoTests
{
    [Fact]
    public void Character_and_word_echo_are_independent()
    {
        var both = KeyEchoSettings.Defaults with { SpeakCharacters = true, SpeakWords = true };
        both.SpeakCharacters.Should().BeTrue();
        both.SpeakWords.Should().BeTrue();

        var neither = KeyEchoSettings.Defaults with { SpeakCharacters = false, SpeakWords = false };
        neither.SpeakCharacters.Should().BeFalse();
        neither.SpeakWords.Should().BeFalse();
    }

    [Fact]
    public void Echo_does_not_extend_into_read_mode_by_default()
    {
        // In Read mode a single letter is a navigation command — "h" jumps to
        // the next heading. Echoing it announces "h" before the heading.
        KeyEchoSettings.Defaults.ApplyEchoInReadMode.Should().BeFalse();
    }

    [Fact]
    public void Read_mode_echo_can_be_opted_into()
    {
        // Some users navigate largely by quick-key and want confirmation that
        // the key registered.
        var settings = KeyEchoSettings.Defaults with { ApplyEchoInReadMode = true };
        settings.ApplyEchoInReadMode.Should().BeTrue();
    }

    [Fact]
    public void The_read_mode_gate_does_not_disturb_the_other_echo_settings()
    {
        var settings = KeyEchoSettings.Defaults with { ApplyEchoInReadMode = true };
        settings.SpeakCommandKeys.Should().BeFalse();
        settings.SpeakWords.Should().BeTrue();
        settings.SpeakDeletedCharacters.Should().BeTrue("deleting is destructive in either mode");
    }
}
