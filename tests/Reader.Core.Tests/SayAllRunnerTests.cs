using FluentAssertions;
using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Speech;
using Aura.Abstractions.Text;
using Aura.Core.Review;
using Xunit;

namespace Aura.Core.Tests;

public class SayAllRunnerTests
{
    private sealed class SingleSurfaceProvider : ITextSurfaceProvider
    {
        private readonly ITextSurface _surface;
        public SingleSurfaceProvider(string text) => _surface = new StringTextSurface(text);
        public ITextSurface? GetSurface(AccessibleNode node) => _surface;
    }

    private static AccessibleNode Node()
        => new(new NodeId("n1"), AccessibleRole.Document, "doc", null, null, AccessibleStates.None, null);

    private static ReviewCursor MakeCursor(string text)
    {
        var c = new ReviewCursor(new SingleSurfaceProvider(text));
        c.SyncTo(Node());
        return c;
    }

    [Fact]
    public async Task StartAsync_emits_lines_starting_from_current_position()
    {
        var cursor = MakeCursor("alpha\nbravo\ncharlie");
        cursor.MoveNextLine(); // now at "bravo"

        var lines = new List<string>();
        var runner = new SayAllRunner(cursor, req =>
        {
            if (req.RawText is { } t)
            {
                lines.Add(t);
            }
            return true;
        }) { LinePauseMs = 0 };

        await runner.StartAsync();
        await Task.Delay(50);

        lines.Should().StartWith("bravo").And.Contain("charlie");
        lines.Should().NotContain("alpha");
    }

    [Fact]
    public async Task StartFromBeginningAsync_rewinds_then_reads_top_to_bottom()
    {
        var cursor = MakeCursor("alpha\nbravo\ncharlie");
        cursor.MoveNextLine(); // mid-document
        cursor.MoveNextLine();

        var lines = new List<string>();
        var runner = new SayAllRunner(cursor, req =>
        {
            if (req.RawText is { } t)
            {
                lines.Add(t);
            }
            return true;
        }) { LinePauseMs = 0 };

        await runner.StartFromBeginningAsync();
        await Task.Delay(50);

        lines.Should().Equal("alpha", "bravo", "charlie");
    }

    [Fact]
    public async Task Cancel_stops_the_pump()
    {
        var cursor = MakeCursor(string.Concat(Enumerable.Range(0, 200).Select(i => $"line{i}\n")));
        var lines = new List<string>();
        var runner = new SayAllRunner(cursor, req =>
        {
            if (req.RawText is { } t)
            {
                lines.Add(t);
            }
            return true;
        }) { LinePauseMs = 25 };

        var task = runner.StartFromBeginningAsync();
        await Task.Delay(40);
        runner.Cancel();
        await task.ContinueWith(_ => { });

        lines.Count.Should().BeLessThan(200);
    }

    [Fact]
    public async Task Reasons_are_ReadAll()
    {
        var cursor = MakeCursor("only line");
        var reasons = new List<SpeechReason>();
        var runner = new SayAllRunner(cursor, req =>
        {
            reasons.Add(req.Reason);
            return true;
        }) { LinePauseMs = 0 };

        await runner.StartFromBeginningAsync();
        await Task.Delay(50);

        reasons.Should().NotBeEmpty().And.OnlyContain(r => r == SpeechReason.ReadAll);
    }
}
