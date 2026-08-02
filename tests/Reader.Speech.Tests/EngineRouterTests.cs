using FluentAssertions;
using Aura.Abstractions.Speech;
using Aura.Speech.Engines;
using Xunit;

namespace Aura.Speech.Tests;

public class EngineRouterTests
{
    [Fact]
    public async Task Initial_engine_is_routed()
    {
        var a = new FakeEngine("a");
        var router = new EngineRouter(a);

        router.Id.Should().Be("a");
        await router.SpeakAsync(MakeUtterance("hello"), CancellationToken.None);
        a.SpokenTexts.Should().ContainSingle().Which.Should().Be("hello");
    }

    [Fact]
    public async Task Switch_routes_subsequent_speak_to_new_engine()
    {
        var a = new FakeEngine("a");
        var b = new FakeEngine("b");
        var router = new EngineRouter(a);

        var previous = router.Switch(b);

        previous.Should().BeSameAs(a);
        router.Id.Should().Be("b");

        await router.SpeakAsync(MakeUtterance("after"), CancellationToken.None);
        a.SpokenTexts.Should().BeEmpty();
        b.SpokenTexts.Should().ContainSingle().Which.Should().Be("after");
    }

    [Fact]
    public void Switch_cancels_previous_engine()
    {
        var a = new FakeEngine("a");
        var b = new FakeEngine("b");
        var router = new EngineRouter(a);

        router.Switch(b);

        a.CancelCount.Should().Be(1);
    }

    [Fact]
    public void Switch_to_same_engine_is_noop()
    {
        var a = new FakeEngine("a");
        var router = new EngineRouter(a);

        router.Switch(a);

        a.CancelCount.Should().Be(0);
    }

    [Fact]
    public void EngineChanged_fires_on_switch()
    {
        var a = new FakeEngine("a");
        var b = new FakeEngine("b");
        var router = new EngineRouter(a);
        ISpeechEngine? notified = null;
        router.EngineChanged += e => notified = e;

        router.Switch(b);

        notified.Should().BeSameAs(b);
    }

    private static SpeechUtterance MakeUtterance(string text)
        => new(text, ProsodyHint.Default, VoiceId: null, SpeechPriority.Next, CancelGroup: null,
            RuleTrace: Array.Empty<string>());

    private sealed class FakeEngine : ISpeechEngine
    {
        public FakeEngine(string id) { Id = id; }
        public string Id { get; }
        public IReadOnlyList<VoiceInfo> Voices { get; } = Array.Empty<VoiceInfo>();
        public string? DefaultVoiceId { get; set; }
        public List<string> SpokenTexts { get; } = new();
        public int CancelCount { get; private set; }

        public ValueTask SpeakAsync(SpeechUtterance utterance, CancellationToken cancellationToken)
        {
            SpokenTexts.Add(utterance.Text);
            return ValueTask.CompletedTask;
        }

        public ValueTask CancelAsync()
        {
            CancelCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
