using Aura.Abstractions.Output;
using Aura.Abstractions.Speech;
using Aura.Output;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Aura.Output.Tests;

/// <summary>
/// Each case here is a duplicate-speech bug found on real hardware. They were
/// each patched at the producer; this is the same logic stated once, where the
/// next producer inherits it instead of colliding.
/// </summary>
public class OutputArbiterTests
{
    /// <summary>An announcement about <paramref name="subject"/> saying <paramref name="text"/>.</summary>
    private static Presentation Pres(SpeechReason reason, string subject, string text)
        => new(
            Segments: [new PresentationSegment(text, SegmentKind.Content)],
            Reason: reason,
            Subject: subject,
            Priority: SpeechPriority.Next,
            CancelGroup: null,
            Validity: null,
            RuleTrace: []);

    private static (OutputArbiter Arbiter, FakeTimeProvider Time) Make()
    {
        var time = new FakeTimeProvider();
        return (new OutputArbiter(time), time);
    }

    [Fact]
    public void Focus_and_selection_for_one_arrow_press_speak_once()
    {
        // A ListBox raises both for a single keypress. This produced
        // "speech, speech" on the settings categories list.
        var (a, _) = Make();
        a.Evaluate(Pres(SpeechReason.FocusChanged, "item-1", "Speech"))
            .Should().Be(OutputDecision.Speak);
        a.Evaluate(Pres(SpeechReason.SelectionChanged, "item-1", "Speech"))
            .Should().Be(OutputDecision.Drop);
    }

    [Fact]
    public void The_same_reason_twice_is_two_real_actions_not_a_duplicate()
    {
        // Arrowing through consecutive blank lines raises CaretMoved each time
        // for the same control, legitimately, and each must be heard. Only a
        // DIFFERENT reason about the same subject means two producers
        // describing one action.
        var (a, _) = Make();
        a.Evaluate(Pres(SpeechReason.CaretMoved, "doc", "blank")).Should().Be(OutputDecision.Speak);
        a.Evaluate(Pres(SpeechReason.CaretMoved, "doc", "blank")).Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void Repeated_identical_text_is_NOT_suppressed()
    {
        // A content-based rule cannot tell "nothing moved" from "the next thing
        // reads the same". Arrowing up through consecutive blank lines
        // legitimately produces "blank" every time, and an earlier version of
        // this class swallowed all but the first — silence where the user
        // needed feedback. Boundary silence comes from the keypress cancelling
        // in-flight speech, not from matching words.
        var (a, _) = Make();
        a.Evaluate(Pres(SpeechReason.CaretMoved, "doc", "blank")).Should().Be(OutputDecision.Speak);
        a.Evaluate(Pres(SpeechReason.CaretMoved, "doc", "blank")).Should().Be(OutputDecision.Speak);
        a.Evaluate(Pres(SpeechReason.CaretMoved, "doc", "blank")).Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void Moving_to_a_genuinely_different_item_still_speaks()
    {
        var (a, _) = Make();
        a.Evaluate(Pres(SpeechReason.SelectionChanged, "a", "General")).Should().Be(OutputDecision.Speak);
        a.Evaluate(Pres(SpeechReason.SelectionChanged, "b", "Speech")).Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void A_user_request_is_never_suppressed_even_if_it_repeats()
    {
        // Pressing "read current line" twice must say the line twice. The user
        // asked, twice, on purpose.
        var (a, _) = Make();
        a.Evaluate(Pres(SpeechReason.ReadLine, "doc", "hello")).Should().Be(OutputDecision.Speak);
        a.Evaluate(Pres(SpeechReason.ReadLine, "doc", "hello")).Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void A_user_request_is_not_dropped_by_a_preceding_navigation()
    {
        var (a, _) = Make();
        a.Evaluate(Pres(SpeechReason.FocusChanged, "n", "Button")).Should().Be(OutputDecision.Speak);
        a.Evaluate(Pres(SpeechReason.ReadLine, "n", "Button")).Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void Ambient_output_never_suppresses_navigation()
    {
        // A toast arriving mid-navigation must not eat the focus announcement.
        var (a, _) = Make();
        a.Evaluate(Pres(SpeechReason.LiveRegionUpdate, "n", "saved")).Should().Be(OutputDecision.Speak);
        a.Evaluate(Pres(SpeechReason.FocusChanged, "n", "OK button")).Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void Navigation_supersedes_a_state_change_for_the_same_control()
    {
        var (a, _) = Make();
        a.Evaluate(Pres(SpeechReason.FocusChanged, "cb", "Enabled, check box")).Should().Be(OutputDecision.Speak);
        a.Evaluate(Pres(SpeechReason.ValueChanged, "cb", "on")).Should().Be(OutputDecision.Drop);
    }

    [Fact]
    public void A_state_change_long_after_the_move_is_still_announced()
    {
        // Checking a box a second after landing on it is a real event, not a
        // duplicate of the focus announcement.
        var (a, time) = Make();
        a.Evaluate(Pres(SpeechReason.FocusChanged, "cb", "Enabled, check box")).Should().Be(OutputDecision.Speak);
        time.Advance(TimeSpan.FromSeconds(1));
        a.Evaluate(Pres(SpeechReason.ValueChanged, "cb", "checked")).Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void Echo_outranks_navigation_so_typing_feedback_is_never_starved()
    {
        var (a, _) = Make();
        a.Evaluate(Pres(SpeechReason.CaretMoved, "edit", "a")).Should().Be(OutputDecision.Speak);
        a.Evaluate(Pres(SpeechReason.UserAnnouncement, "edit", "b"))
            .Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void Reset_clears_the_coincidence_state()
    {
        var (a, _) = Make();
        a.Evaluate(Pres(SpeechReason.FocusChanged, "x", "One")).Should().Be(OutputDecision.Speak);
        a.Reset();
        // Without the reset this would lose to the focus announcement above.
        a.Evaluate(Pres(SpeechReason.ValueChanged, "x", "Two")).Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void Announcements_about_different_subjects_never_suppress_each_other()
    {
        var (a, _) = Make();
        a.Evaluate(Pres(SpeechReason.FocusChanged, "a", "One")).Should().Be(OutputDecision.Speak);
        a.Evaluate(Pres(SpeechReason.ValueChanged, "b", "Two")).Should().Be(OutputDecision.Speak);
    }

    [Theory]
    [InlineData(SpeechReason.ReadLine, OutputCategory.UserRequested)]
    [InlineData(SpeechReason.FocusChanged, OutputCategory.Navigation)]
    [InlineData(SpeechReason.ValueChanged, OutputCategory.StateChange)]
    [InlineData(SpeechReason.LiveRegionUpdate, OutputCategory.Ambient)]
    [InlineData(SpeechReason.Unknown, OutputCategory.Ambient)]
    public void Reasons_map_to_the_category_that_governs_them(SpeechReason reason, OutputCategory expected)
    {
        // Unknown must land on Ambient: an unmapped reason may not outrank a
        // real one, but must still be heard.
        OutputArbiter.Categorize(reason).Should().Be(expected);
    }
}
