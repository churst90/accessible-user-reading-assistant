using FluentAssertions;
using OpenReader.Abstractions.Input;
using OpenReader.Abstractions.Navigation;
using OpenReader.Input.Commands;
using OpenReader.Input.Gestures;
using Xunit;

namespace OpenReader.Input.Tests;

/// <summary>
/// Layering is what makes Read mode possible. Without it, <c>h</c> either
/// breaks typing everywhere or does nothing at all.
/// </summary>
public class GestureLayerTests
{
    private const int VK_H = 0x48;
    private const int VK_K = 0x4B;

    private static RawInput Key(int vk, InputModifiers mods = InputModifiers.None)
        => new(InputSource.Keyboard, InputEventKind.KeyDown, vk, mods, DateTimeOffset.UtcNow);

    [Fact]
    public void A_read_mode_binding_does_nothing_while_typing()
    {
        // The whole point: pressing "h" in a form field must type an h.
        var map = new GestureMap();
        map.Bind(GestureMap.ReadModeLayer, new KeyChord(VK_H, InputModifiers.None), ReaderCommand.ReadNextLine);

        map.Resolve(Key(VK_H), new GestureContext(ReaderMode.Type))
            .Should().Be(ReaderCommand.None);
    }

    [Fact]
    public void The_same_binding_fires_while_reading()
    {
        var map = new GestureMap();
        map.Bind(GestureMap.ReadModeLayer, new KeyChord(VK_H, InputModifiers.None), ReaderCommand.ReadNextLine);

        map.Resolve(Key(VK_H), new GestureContext(ReaderMode.Read))
            .Should().Be(ReaderCommand.ReadNextLine);
    }

    [Fact]
    public void The_default_context_is_type_mode_so_unknown_situations_never_swallow_keys()
    {
        // Swallowing a keystroke is far worse than missing a shortcut: the
        // user's typing silently vanishes and they cannot see why. This
        // depends on ReaderMode.Type being the zero value — a default struct
        // must not land in Read mode.
        default(GestureContext).Mode.Should().Be(ReaderMode.Type);

        var map = new GestureMap();
        map.Bind(GestureMap.ReadModeLayer, new KeyChord(VK_H, InputModifiers.None), ReaderCommand.ReadNextLine);

        map.Resolve(Key(VK_H)).Should().Be(ReaderCommand.None);
        map.Resolve(Key(VK_H), default).Should().Be(ReaderCommand.None);
    }

    [Fact]
    public void User_bindings_override_defaults()
    {
        var map = new GestureMap();
        var chord = new KeyChord(VK_K, InputModifiers.Reader);
        map.Bind(chord, ReaderCommand.ReadLine);
        map.Bind(GestureMap.UserLayer, chord, ReaderCommand.StopSpeech);

        map.Resolve(Key(VK_K, InputModifiers.Reader)).Should().Be(ReaderCommand.StopSpeech);
    }

    [Fact]
    public void App_bindings_override_user_bindings_but_only_in_that_app()
    {
        var map = new GestureMap();
        var chord = new KeyChord(VK_K, InputModifiers.Reader);
        map.Bind(GestureMap.UserLayer, chord, ReaderCommand.ReadLine);
        map.AddAppLayer("excel");
        map.Bind(GestureMap.AppLayerName("excel"), chord, ReaderCommand.ReportTitle);

        map.Resolve(Key(VK_K, InputModifiers.Reader), new GestureContext(AppExecutableName: "excel"))
            .Should().Be(ReaderCommand.ReportTitle);
        map.Resolve(Key(VK_K, InputModifiers.Reader), new GestureContext(AppExecutableName: "notepad"))
            .Should().Be(ReaderCommand.ReadLine);
    }

    [Fact]
    public void App_matching_ignores_case()
    {
        var map = new GestureMap();
        map.AddAppLayer("Excel");
        map.Bind(GestureMap.AppLayerName("Excel"), new KeyChord(VK_K, InputModifiers.None), ReaderCommand.ReportTitle);

        map.Resolve(Key(VK_K), new GestureContext(AppExecutableName: "EXCEL"))
            .Should().Be(ReaderCommand.ReportTitle);
    }

    [Fact]
    public void Read_mode_wins_over_an_app_binding_for_the_same_chord()
    {
        // Specificity order: readmode > app > user > default.
        var map = new GestureMap();
        var chord = new KeyChord(VK_H, InputModifiers.None);
        map.AddAppLayer("chrome");
        map.Bind(GestureMap.AppLayerName("chrome"), chord, ReaderCommand.ReportTitle);
        map.Bind(GestureMap.ReadModeLayer, chord, ReaderCommand.ReadNextLine);

        map.Resolve(Key(VK_H), new GestureContext(ReaderMode.Read, "chrome"))
            .Should().Be(ReaderCommand.ReadNextLine);
    }

    [Fact]
    public void Redeclaring_a_layer_keeps_its_bindings()
    {
        // Layers get declared by the host and populated by config load, in
        // whichever order those happen to run.
        var map = new GestureMap();
        map.Bind("custom", new KeyChord(VK_K, InputModifiers.None), ReaderCommand.ReadLine);
        map.AddLayer("custom", 500, _ => true);

        map.Resolve(Key(VK_K)).Should().Be(ReaderCommand.ReadLine);
    }

    [Fact]
    public void Clearing_a_layer_leaves_the_others_intact()
    {
        var map = new GestureMap();
        var chord = new KeyChord(VK_K, InputModifiers.Reader);
        map.Bind(chord, ReaderCommand.ReadLine);
        map.Bind(GestureMap.UserLayer, chord, ReaderCommand.StopSpeech);

        map.ClearLayer(GestureMap.UserLayer);

        map.Resolve(Key(VK_K, InputModifiers.Reader)).Should().Be(ReaderCommand.ReadLine);
    }

    [Fact]
    public void Snapshot_flattens_to_what_would_actually_fire()
    {
        var map = new GestureMap();
        var chord = new KeyChord(VK_K, InputModifiers.Reader);
        map.Bind(chord, ReaderCommand.ReadLine);
        map.Bind(GestureMap.UserLayer, chord, ReaderCommand.StopSpeech);

        map.Snapshot()[chord].Should().Be(ReaderCommand.StopSpeech);
    }

    [Fact]
    public void Snapshot_of_all_layers_sees_bindings_no_context_activates()
    {
        // Documentation and conflict reporting must see Read-mode bindings
        // even when the reader is not currently in Read mode.
        var map = new GestureMap();
        map.Bind(GestureMap.ReadModeLayer, new KeyChord(VK_H, InputModifiers.None), ReaderCommand.ReadNextLine);

        map.SnapshotAllLayers()
            .Should().Contain(b => b.Layer == GestureMap.ReadModeLayer && b.Command == ReaderCommand.ReadNextLine);
    }

    [Fact]
    public void Existing_flat_bindings_still_resolve_unchanged()
    {
        // The built-in defaults and every existing caller use the unlayered
        // overloads; layering had to be purely additive.
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, KeyboardLayout.Desktop);

        map.Resolve(Key(0x68 /* NUMPAD8 */)).Should().Be(ReaderCommand.ReadLine);
    }
}
