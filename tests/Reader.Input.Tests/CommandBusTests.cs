using FluentAssertions;
using Aura.Input.Commands;
using Xunit;

namespace Aura.Input.Tests;

public class CommandBusTests
{
    [Fact]
    public async Task Bind_then_dispatch_invokes_handler()
    {
        var bus = new CommandBus();
        var hits = 0;
        bus.Bind(ReaderCommand.ReadLine, _ => { hits++; return ValueTask.CompletedTask; });

        await bus.DispatchAsync(ReaderCommand.ReadLine);
        await bus.DispatchAsync(ReaderCommand.ReadLine);

        hits.Should().Be(2);
    }

    [Fact]
    public async Task Multiple_handlers_all_fire()
    {
        var bus = new CommandBus();
        var a = 0;
        var b = 0;
        bus.Bind(ReaderCommand.StopSpeech, _ => { a++; return ValueTask.CompletedTask; });
        bus.Bind(ReaderCommand.StopSpeech, _ => { b++; return ValueTask.CompletedTask; });

        await bus.DispatchAsync(ReaderCommand.StopSpeech);

        a.Should().Be(1);
        b.Should().Be(1);
    }

    [Fact]
    public async Task Unsubscribe_removes_handler()
    {
        var bus = new CommandBus();
        var hits = 0;
        var sub = bus.Bind(ReaderCommand.ReadLine, _ => { hits++; return ValueTask.CompletedTask; });

        sub.Dispose();
        await bus.DispatchAsync(ReaderCommand.ReadLine);

        hits.Should().Be(0);
    }

    [Fact]
    public async Task Dispatch_None_is_a_noop()
    {
        var bus = new CommandBus();
        var hits = 0;
        bus.Bind(ReaderCommand.ReadLine, _ => { hits++; return ValueTask.CompletedTask; });

        await bus.DispatchAsync(ReaderCommand.None);

        hits.Should().Be(0);
    }

    [Fact]
    public async Task Handler_exception_is_swallowed_and_others_run()
    {
        var bus = new CommandBus();
        var hits = 0;
        bus.Bind(ReaderCommand.ReadLine, _ => throw new InvalidOperationException("boom"));
        bus.Bind(ReaderCommand.ReadLine, _ => { hits++; return ValueTask.CompletedTask; });

        await bus.DispatchAsync(ReaderCommand.ReadLine);

        hits.Should().Be(1);
    }
}
