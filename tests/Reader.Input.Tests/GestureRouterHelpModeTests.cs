using FluentAssertions;
using OpenReader.Abstractions.Input;
using OpenReader.Input.Commands;
using OpenReader.Input.Gestures;
using Xunit;

namespace OpenReader.Input.Tests;

public class GestureRouterHelpModeTests
{
    private sealed class FakeSource : IInputSource
    {
        public event EventHandler<RawInput>? RawInputReceived;
        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Fire(RawInput input) => RawInputReceived?.Invoke(this, input);
    }

    private static RawInput Down(int vk, InputModifiers mods = InputModifiers.None)
        => new(InputSource.Keyboard, InputEventKind.KeyDown, vk, mods, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Help_mode_intercepts_chords_and_does_not_dispatch()
    {
        var source = new FakeSource();
        var map = new GestureMap();
        map.Bind(new KeyChord(0x28 /* DOWN */, InputModifiers.Reader), ReaderCommand.ReadNextLine);
        map.Bind(new KeyChord(0x31 /* 1 */, InputModifiers.Reader), ReaderCommand.ToggleKeyboardHelp);

        var bus = new CommandBus();
        var dispatched = new List<ReaderCommand>();
        bus.Bind(ReaderCommand.ReadNextLine, _ => { dispatched.Add(ReaderCommand.ReadNextLine); return ValueTask.CompletedTask; });
        bus.Bind(ReaderCommand.ToggleKeyboardHelp, _ => { dispatched.Add(ReaderCommand.ToggleKeyboardHelp); return ValueTask.CompletedTask; });

        var announced = new List<ReaderCommand>();
        using var router = new GestureRouter(source, map, bus);
        router.SetHelpAnnouncer(c => announced.Add(c));
        router.Start();

        // Enter help mode.
        source.Fire(Down(0x31, InputModifiers.Reader));
        await Task.Delay(20);
        router.KeyboardHelpMode.Should().BeTrue();

        // Now Reader+Down should be announced, NOT dispatched.
        source.Fire(Down(0x28, InputModifiers.Reader));
        await Task.Delay(20);
        announced.Should().Contain(ReaderCommand.ReadNextLine);
        dispatched.Should().NotContain(ReaderCommand.ReadNextLine);

        // Reader+1 again exits help mode.
        source.Fire(Down(0x31, InputModifiers.Reader));
        await Task.Delay(20);
        router.KeyboardHelpMode.Should().BeFalse();

        // Reader+Down dispatches normally now.
        source.Fire(Down(0x28, InputModifiers.Reader));
        await Task.Delay(20);
        dispatched.Should().Contain(ReaderCommand.ReadNextLine);
    }
}
