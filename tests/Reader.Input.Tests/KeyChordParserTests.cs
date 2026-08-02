using FluentAssertions;
using Aura.Abstractions.Input;
using Aura.Input.Gestures;
using Xunit;

namespace Aura.Input.Tests;

public class KeyChordParserTests
{
    [Theory]
    [InlineData("Reader+Down", 0x28, InputModifiers.Reader)]
    [InlineData("Reader+Ctrl+Right", 0x27, InputModifiers.Reader | InputModifiers.Control)]
    [InlineData("Insert+O", 0x4F, InputModifiers.Reader)]
    [InlineData("CapsLock+A", 0x41, InputModifiers.Reader)]
    [InlineData("Ctrl+Shift+P", 0x50, InputModifiers.Control | InputModifiers.Shift)]
    [InlineData("F1", 0x70, InputModifiers.None)]
    [InlineData("Reader+F12", 0x7B, InputModifiers.Reader)]
    [InlineData("NumPad4", 0x64, InputModifiers.None)]
    [InlineData("Ctrl+NumPad6", 0x66, InputModifiers.Control)]
    [InlineData("Reader+Period", 0xBE, InputModifiers.Reader)]
    [InlineData("reader+down", 0x28, InputModifiers.Reader)]
    [InlineData("  Reader + Down  ", 0x28, InputModifiers.Reader)]
    public void Parses_valid_chords(string text, int vk, InputModifiers mods)
    {
        KeyChordParser.TryParse(text, out var chord).Should().BeTrue();
        chord.KeyCode.Should().Be(vk);
        chord.Modifiers.Should().Be(mods);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bogus")]
    [InlineData("Reader+Bogus")]
    [InlineData("Reader+")]
    public void Rejects_invalid_chords(string text)
    {
        KeyChordParser.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void Format_produces_canonical_string()
    {
        var chord = new KeyChord(0x28, InputModifiers.Reader | InputModifiers.Control);
        KeyChordParser.Format(chord).Should().Be("Reader+Ctrl+Down");
    }

    [Fact]
    public void Round_trips()
    {
        var original = new KeyChord(0x4F, InputModifiers.Reader);
        var formatted = KeyChordParser.Format(original);
        KeyChordParser.TryParse(formatted, out var roundtrip).Should().BeTrue();
        roundtrip.Should().Be(original);
    }
}
