using FluentAssertions;
using Aura.Abstractions.Input;
using Aura.Input.Commands;
using Aura.Input.Gestures;
using Xunit;

namespace Aura.Input.Tests;

public class KeyBindingApplierTests
{
    private static RawInput Down(int vk, InputModifiers mods = InputModifiers.None)
        => new(InputSource.Keyboard, InputEventKind.KeyDown, vk, mods, DateTimeOffset.UtcNow);

    [Fact]
    public void Override_rebinds_an_existing_chord_to_a_new_command()
    {
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Desktop);
        // By default Reader+A → SayAllFromCursor.
        var overrides = new Dictionary<string, string>
        {
            ["Reader+A"] = "ReportTitle",
        };

        KeyBindingApplier.ApplyOverrides(map, overrides);

        map.Resolve(Down(0x41, InputModifiers.Reader)).Should().Be(ReaderCommand.ReportTitle);
    }

    [Fact]
    public void Unparseable_chord_is_skipped_not_thrown()
    {
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Desktop);
        var overrides = new Dictionary<string, string>
        {
            ["Bogus+Whatever"] = "ReportTitle",
            ["Reader+A"] = "ReportTitle",
        };

        var applied = KeyBindingApplier.ApplyOverrides(map, overrides);
        applied.Should().Be(1);
    }

    [Fact]
    public void Unknown_command_is_skipped()
    {
        var map = new GestureMap();
        var overrides = new Dictionary<string, string>
        {
            ["Reader+X"] = "DoesNotExist",
        };
        var applied = KeyBindingApplier.ApplyOverrides(map, overrides);
        applied.Should().Be(0);
    }

    [Fact]
    public void Null_overrides_is_a_noop()
    {
        var map = new GestureMap();
        KeyBindingApplier.ApplyOverrides(map, null).Should().Be(0);
    }
}
