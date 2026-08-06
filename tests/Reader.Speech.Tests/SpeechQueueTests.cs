using FluentAssertions;
using Aura.Abstractions.Output;
using Aura.Abstractions.Speech;
using Aura.Speech.Queue;
using Xunit;

namespace Aura.Speech.Tests;

public class SpeechQueueTests
{
    private static Utterance Make(
        string text,
        SpeechPriority priority = SpeechPriority.Next,
        string? cancelGroup = null,
        IValidityPredicate? validity = null)
        => new([new TextPart(text)], priority, cancelGroup, validity, RuleTrace: Array.Empty<string>());

    /// <summary>A predicate whose answer the test controls.</summary>
    private sealed class Gate(bool valid) : IValidityPredicate
    {
        public bool Valid { get; set; } = valid;
        public bool IsStillValid() => Valid;
    }

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

        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Spoken().Should().Be("next");
        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Spoken().Should().Be("next later");
        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Spoken().Should().Be("background");
        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Spoken().Should().Be("background later");
    }

    [Fact]
    public void Cancel_group_drops_pending_items_in_same_group()
    {
        using var q = new SpeechQueue();
        q.Enqueue(Make("first focus", cancelGroup: "focus"));
        q.Enqueue(Make("second focus", cancelGroup: "focus"));
        q.Enqueue(Make("third focus", cancelGroup: "focus"));

        q.Count.Should().Be(1);
        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Spoken().Should().Be("third focus");
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
        Utterance? preempted = null;
        q.PreemptiveEnqueued += u => preempted = u;

        q.SetCurrentSpeakingGroup("focus");
        q.Enqueue(Make("second focus", cancelGroup: "focus"));

        preempted.Should().NotBeNull();
        preempted!.Spoken().Should().Be("second focus");
    }

    [Fact]
    public void Cancel_group_match_without_matching_in_flight_does_not_preempt()
    {
        // If the engine is speaking a UserAnnouncement (no cancel group), a
        // focus change shouldn't cut it off.
        using var q = new SpeechQueue();
        Utterance? preempted = null;
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
        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Spoken().Should().Be("alert");
        q.WaitForNext(TimeSpan.FromMilliseconds(50))!.Spoken().Should().Be("focus 2");
    }

    [Fact]
    public void Identical_words_are_not_a_duplicate_and_are_not_suppressed()
    {
        // This asserts the opposite of what it used to. The queue dropped a
        // second enqueue whose group and text matched the first within a
        // window, which is the same content-based mistake the arbiter had
        // already had removed from it — and it swallowed the second of two
        // consecutive blank lines and every unnamed toolbar button after the
        // first. Words cannot distinguish "sent twice" from "reads the same".
        //
        // Note the queue still ends up with one item here, because they share a
        // cancel group and the later one supersedes the earlier. That is
        // suppression by identity, which is correct, and it is why removing the
        // content rule costs nothing for the case it was meant to cover.
        using var q = new SpeechQueue();
        q.Enqueue(Make("OK, button", cancelGroup: "focus")).Should().BeTrue();
        q.Enqueue(Make("OK, button", cancelGroup: "focus")).Should().BeTrue();

        q.Count.Should().Be(1);
    }

    [Fact]
    public void The_same_words_twice_with_no_cancel_group_are_both_heard()
    {
        // Arrowing up through two consecutive blank lines, once speech for the
        // first has already played.
        using var q = new SpeechQueue();
        q.Enqueue(Make("blank")).Should().BeTrue();
        q.WaitForNext(TimeSpan.FromMilliseconds(50)).Spoken().Should().Be("blank");
        q.Enqueue(Make("blank")).Should().BeTrue();

        q.WaitForNext(TimeSpan.FromMilliseconds(50)).Spoken().Should().Be("blank");
    }

    [Fact]
    public void Now_priority_goes_first_but_does_not_delete_what_is_waiting()
    {
        // This asserted the opposite until it was heard. Now used to drop every
        // pending lower-priority item, so opening a dialog silenced the control
        // its focus landed on: the window title is a Now announcement and it
        // threw away the announcement of where the user actually was.
        //
        // Interrupting is the point of Now. Deleting the user's place is not —
        // those queued items are things they still need to hear.
        using var q = new SpeechQueue();
        Utterance? preempted = null;
        q.PreemptiveEnqueued += u => preempted = u;

        q.Enqueue(Make("background", SpeechPriority.Background));
        q.Enqueue(Make("next", SpeechPriority.Next));
        q.Enqueue(Make("alert", SpeechPriority.Now));

        q.Count.Should().Be(3);
        preempted.Should().NotBeNull();
        preempted!.Spoken().Should().Be("alert");

        q.WaitForNext(TimeSpan.FromMilliseconds(50)).Spoken().Should().Be("alert");
        q.WaitForNext(TimeSpan.FromMilliseconds(50)).Spoken().Should().Be("next");
        q.WaitForNext(TimeSpan.FromMilliseconds(50)).Spoken().Should().Be("background");
    }

    [Fact]
    public async Task DequeueAsync_blocks_until_enqueue()
    {
        using var q = new SpeechQueue();
        var dequeueTask = q.DequeueAsync(CancellationToken.None);
        dequeueTask.IsCompleted.Should().BeFalse();

        q.Enqueue(Make("hello"));

        var u = await dequeueTask;
        u.Spoken().Should().Be("hello");
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

    [Fact]
    public void SweepInvalid_drops_only_the_announcements_whose_reason_has_passed()
    {
        // The replacement for cancelling speech on every keypress. Cancelling
        // on input cannot tell a stale announcement from a valid one that
        // happens to be queued behind it, which is how the same fix produced
        // speech running an item behind AND silence on backspace, in turn.
        using var q = new SpeechQueue();
        var gone = new Gate(false);
        var here = new Gate(true);

        q.Enqueue(Make("the item you just left", validity: gone));
        q.Enqueue(Make("the item you are on", validity: here));
        q.Enqueue(Make("a toast nobody focused"));

        q.SweepInvalid().Should().Be(1);
        q.Count.Should().Be(2);
        q.WaitForNext(TimeSpan.FromMilliseconds(50)).Spoken().Should().Be("the item you are on");
        q.WaitForNext(TimeSpan.FromMilliseconds(50)).Spoken().Should().Be("a toast nobody focused");
    }

    [Fact]
    public void An_announcement_with_no_predicate_is_never_swept()
    {
        // Anything the user pressed a key to hear. Silence in answer to a
        // keystroke is never right.
        using var q = new SpeechQueue();
        q.Enqueue(Make("read current line"));

        q.SweepInvalid().Should().Be(0);
        q.Count.Should().Be(1);
    }

    [Fact]
    public void Sweeping_clears_the_coalesce_memory_so_the_same_words_can_return()
    {
        // Otherwise a swept announcement keeps suppressing its own
        // re-announcement when the user comes back to the control.
        using var q = new SpeechQueue();
        var gate = new Gate(true);
        q.Enqueue(Make("Documents", cancelGroup: "focus", validity: gate));
        gate.Valid = false;
        q.SweepInvalid().Should().Be(1);

        q.Enqueue(Make("Documents", cancelGroup: "focus")).Should().BeTrue();
        q.Count.Should().Be(1);
    }

    [Fact]
    public void Something_more_important_preempts_what_is_playing()
    {
        // A desktop icon's tooltip is Background and starts speaking the moment
        // it appears. The icon's own name is Navigation — the thing the user
        // asked for by arrowing — and queueing it behind a sentence of
        // description about a control they have not been told the name of yet
        // is what "the tooltip steps on the icon label" sounded like.
        using var q = new SpeechQueue();
        Utterance? preempted = null;
        q.PreemptiveEnqueued += u => preempted = u;
        q.SetCurrentSpeaking("tooltip", SpeechPriority.Background);

        q.Enqueue(Make("Recycle Bin, list item", SpeechPriority.Next, cancelGroup: "focus"));

        preempted.Should().NotBeNull();
        preempted.Spoken().Should().Be("Recycle Bin, list item");
    }

    [Fact]
    public void Something_less_important_waits_its_turn()
    {
        using var q = new SpeechQueue();
        var preempted = false;
        q.PreemptiveEnqueued += _ => preempted = true;
        q.SetCurrentSpeaking("focus", SpeechPriority.Next);

        q.Enqueue(Make("Contains the files you have deleted", SpeechPriority.Background, cancelGroup: "tooltip"));

        preempted.Should().BeFalse();
    }
}
