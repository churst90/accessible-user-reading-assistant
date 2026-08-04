using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Input;
using Aura.Abstractions.Text;
using Aura.Core.Text;
using FluentAssertions;
using Xunit;

namespace Aura.Core.Tests.Text;

/// <summary>
/// What a resolved caret motion is actually announced as.
/// </summary>
/// <remarks>
/// Every case here was reported by ear on Windows and could not have been
/// caught anywhere else in the pipeline, because by the time the announcement
/// left this class it was a string of characters that happened to make no
/// sound.
/// </remarks>
public class CaretAnnouncementTests
{
    private static readonly AccessibleNode Doc = new(
        new NodeId("doc"), AccessibleRole.Document, "Untitled", null, null,
        AccessibleStates.None, null);

    private static string? Announced(CaretMotionKind kind, string? text)
        => CaretFollowService.ToRequest(new CaretMotion(kind, text ?? string.Empty), Doc)?.RawText;

    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("   ")]
    public void An_empty_line_says_blank_however_the_provider_spells_empty(string text)
    {
        // The bug: a provider asked to expand an empty line hands back the line
        // terminator itself. "\r\n" is not empty, so the old IsNullOrEmpty
        // check let it through, and the reader spoke two characters that make
        // no sound — indistinguishable from a failure to read at all.
        Announced(CaretMotionKind.Line, text).Should().Be("blank");
    }

    [Fact]
    public void A_line_is_announced_without_its_terminator()
    {
        Announced(CaretMotionKind.Line, "hello\r\n").Should().Be("hello");
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    public void Landing_past_the_last_character_says_line_feed(string text)
    {
        Announced(CaretMotionKind.Character, text).Should().Be("line feed");
    }

    [Fact]
    public void Landing_on_whitespace_between_words_says_nothing()
    {
        Announced(CaretMotionKind.Word, "  ").Should().BeNull();
    }

    // ---- which unit a key asks for ----

    private static TextUnit? UnitFor(int vk, InputModifiers modifiers = InputModifiers.None)
        => CaretFollowService.RequestedUnit(new RawInput(
            InputSource.Keyboard, InputEventKind.KeyDown, vk, modifiers, DateTimeOffset.UnixEpoch));

    [Theory]
    [InlineData(0x25)] // Left
    [InlineData(0x27)] // Right
    [InlineData(0x24)] // Home
    [InlineData(0x23)] // End
    public void Horizontal_keys_ask_for_a_character(int vk)
        => UnitFor(vk).Should().Be(TextUnit.Character);

    [Theory]
    [InlineData(0x26)] // Up
    [InlineData(0x28)] // Down
    [InlineData(0x21)] // PageUp
    [InlineData(0x22)] // PageDown
    public void Vertical_keys_ask_for_a_line(int vk)
        => UnitFor(vk).Should().Be(TextUnit.Line);

    [Theory]
    [InlineData(0x25)]
    [InlineData(0x27)]
    public void Control_makes_a_horizontal_move_a_word(int vk)
        => UnitFor(vk, InputModifiers.Control).Should().Be(TextUnit.Word);

    [Theory]
    [InlineData(0x08)] // Backspace — changes the document; key echo owns it
    [InlineData(0x2E)] // Delete
    [InlineData(0x41)] // A
    public void Keys_that_must_not_drive_the_caret_ask_for_nothing(int vk)
        => UnitFor(vk).Should().BeNull();
}
