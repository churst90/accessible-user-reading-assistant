using FluentAssertions;
using OpenReader.Input.Echo;
using Xunit;

namespace OpenReader.Input.Tests;

/// <summary>
/// Deletion feedback is not character echo.
/// </summary>
/// <remarks>
/// A sighted user glances at the line to see what disappeared. Without this
/// announcement the only way to find out is to navigate back over the text and
/// re-read it — so a user who finds per-character echo too chatty while typing
/// still needs to know what they just destroyed. The two settings are
/// therefore independent, and deletion defaults on.
/// </remarks>
public class DeletionEchoTests
{
    [Fact]
    public void Deletion_echo_is_on_by_default()
    {
        KeyEchoSettings.Defaults.SpeakDeletedCharacters.Should().BeTrue();
    }

    [Fact]
    public void Character_echo_is_off_by_default_but_deletion_echo_is_not()
    {
        // The asymmetry is the whole point: typing echo is a preference,
        // deletion feedback is closer to a necessity.
        var defaults = KeyEchoSettings.Defaults;
        defaults.SpeakCharacters.Should().BeFalse();
        defaults.SpeakDeletedCharacters.Should().BeTrue();
    }

    [Fact]
    public void Turning_character_echo_off_leaves_deletion_echo_alone()
    {
        var settings = KeyEchoSettings.Defaults with { SpeakCharacters = false };
        settings.SpeakDeletedCharacters.Should().BeTrue();
    }

    [Fact]
    public void Deletion_echo_can_still_be_turned_off_deliberately()
    {
        var settings = KeyEchoSettings.Defaults with { SpeakDeletedCharacters = false };
        settings.SpeakDeletedCharacters.Should().BeFalse();
        settings.SpeakWords.Should().BeTrue("other settings must be unaffected");
    }
}
