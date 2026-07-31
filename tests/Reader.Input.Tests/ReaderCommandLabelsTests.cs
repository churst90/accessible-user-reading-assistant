using FluentAssertions;
using OpenReader.Input.Commands;
using Xunit;

namespace OpenReader.Input.Tests;

/// <summary>
/// Locks down user-facing labels for every <see cref="ReaderCommand"/>. These
/// strings are spoken by keyboard-help mode and shown in the rebind UI; they
/// are part of the user contract.
/// </summary>
public class ReaderCommandLabelsTests
{
    [Fact]
    public void Every_command_has_a_human_label()
    {
        foreach (var command in Enum.GetValues<ReaderCommand>())
        {
            if (command == ReaderCommand.None)
            {
                continue;
            }
            var label = ReaderCommandLabels.Humanize(command);
            label.Should().NotBeNullOrWhiteSpace($"command {command} should have a label");
            // Should not just be the enum name verbatim — that's the fallback path.
            label.Should().NotContain("ReaderCommand", "labels should not echo the type name");
        }
    }

    [Theory]
    [InlineData(ReaderCommand.StopSpeech, "stop speech")]
    [InlineData(ReaderCommand.OpenSettings, "open settings")]
    [InlineData(ReaderCommand.OpenExitDialog, "exit")]
    [InlineData(ReaderCommand.ReadNextLine, "next line")]
    public void Selected_commands_have_stable_labels(ReaderCommand command, string expected)
    {
        ReaderCommandLabels.Humanize(command).Should().Be(expected);
    }
}
