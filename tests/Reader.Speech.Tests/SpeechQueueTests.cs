using FluentAssertions;
using Aura.Abstractions.Speech;
using Aura.Speech.Queue;
using Xunit;

namespace Aura.Speech.Tests;

public class SpeechQueueTests
{
    private static SpeechUtterance Make(string text, SpeechPriority priority = SpeechPriority.Next, string? cancelGroup = null)
        => new(text, ProsodyHint.Default, VoiceId: null, priority, cancelGroup, RuleTrace: Array.Empty<string>());

    [Fact]
    public void Enqueue_then_dequeue_in_priority_order()
    {
        // Now preemption drops pending non-Now items by design, so this test
        // only mixes Next and Background to assert priority ordering.
        using var q = new SpeechQueue();
        q.Enqueue(Make("background", SpeechPriority.Background));
        q.Enqueue(Make("next", SpeechPriority.Next));
        q.Enqueue(Make("background later", SpeechPriority.Background));
        q.Enqueue(Make("next later", SpeechPriority.Next));

        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Text.Should().Be("next");
        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Text.Should().Be("next later");
        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Text.Should().Be("background");
        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Text.Should().Be("background later");
    }

    [Fact]
    public void Cancel_group_drops_pending_items_in_same_group()
    {
        using var q = new SpeechQueue();
        q.Enqueue(Make("first focus", cancelGroup: "focus"));
        q.Enqueue(Make("second focus", cancelGroup: "focus"));
        q.Enqueue(Make("third focus", cancelGroup: "focus"));

        q.Count.Should().Be(1);
        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Text.Should().Be("third focus");
    }

    [Fact]
    public void Cancel_group_match_with_in_flight_speech_fires_preempt()
    {
        // While the engine is mid-utterance speaking "first focus", a new
        // "second focus" arrives. The queue should fire PreemptiveEnqueued so
        // the engine cancels and the new utterance starts immediately —
        // otherwise focus changes feel sluggish (you hear the previous icon
        // finish before the new one starts).
        using var q = new SpeechQueue();
        SpeechUtterance? preempted = null;
        q.PreemptiveEnqueued += u => preempted = u;

        q.SetCurrentSpeakingGroup("focus");
        q.Enqueue(Make("second focus", cancelGroup: "focus"));

        preempted.Should().NotBeNull();
        preempted!.Text.Should().Be("second focus");
    }

    [Fact]
    public void Cancel_group_match_without_matching_in_flight_does_not_preempt()
    {
        // If the engine is speaking a UserAnnouncement (no cancel group), a
        // focus change shouldn't cut it off.
        using var q = new SpeechQueue();
        SpeechUtterance? preempted = null;
        q.PreemptiveEnqueued += u => preempted = u;

        q.SetCurrentSpeakingGroup(null);
        q.Enqueue(Make("focus utterance", cancelGroup: "focus"));

        preempted.Should().BeNull();
    }

    [Fact]
    public void Cancel_group_only_drops_within_group()
    {
        using var q = new SpeechQueue();
        q.Enqueue(Make("alert", cancelGroup: "alert"));
        q.Enqueue(Make("focus 1", cancelGroup: "focus"));
        q.Enqueue(Make("focus 2", cancelGroup: "focus"));

        q.Count.Should().Be(2);
        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Text.Should().Be("alert");
        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Text.Should().Be("focus 2");
    }

    [Fact]
    public void Coalesce_drops_identical_consecutive_enqueue_within_window()
    {
        using var q = new SpeechQueue(coalesceWindow: TimeSpan.FromSeconds(1));
        var queued1 = q.Enqueue(Make("OK, button", cancelGroup: "focus"));
        var queued2 = q.Enqueue(Make("OK, button", cancelGroup: "focus"));

        queued1.Should().BeTrue();
        queued2.Should().BeFalse();
        q.Count.Should().Be(1);
    }

    [Fact]
    public void Now_priority_drops_pending_non_now_and_raises_event()
    {
        using var q = new SpeechQueue();
        SpeechUtterance? preempted = null;
        q.PreemptiveEnqueued += u => preempted = u;

        q.Enqueue(Make("background", SpeechPriority.Background));
        q.Enqueue(Make("next", SpeechPriority.Next));
        q.Enqueue(Make("alert", SpeechPriority.Now));

        q.Count.Should().Be(1);
        preempted.Should().NotBeNull();
        preempted!.Text.Should().Be("alert");
    }

    [Fact]
    public async Task DequeueAsync_blocks_until_enqueue()
    {
        using var q = new SpeechQueue();
        var dequeueTask = q.DequeueAsync(CancellationToken.None);
        dequeueTask.IsCompleted.Should().BeFalse();

        q.Enqueue(Make("hello"));

        var u = await dequeueTask;
        u.Text.Should().Be("hello");
    }

    [Fact]
    public void Clear_drops_all_pending()
    {
        using var q = new SpeechQueue();
        q.Enqueue(Make("a"));
        q.Enqueue(Make("b"));
        q.Clear();
        q.IsEmpty.Should().BeTrue();
        q.WaitForNext(TimeSpan.FromMilliseconds(20)).Should().BeNull();
    }
}
