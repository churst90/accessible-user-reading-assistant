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
    private static SpeechRequest Req(SpeechReason reason)
        => new(reason, Node: null, RawText: "x", AppExecutableName: null);

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
        a.Evaluate(Req(SpeechReason.FocusChanged), "item-1", "Speech")
            .Should().Be(OutputDecision.Speak);
        a.Evaluate(Req(SpeechReason.SelectionChanged), "item-1", "Speech")
            .Should().Be(OutputDecision.Drop);
    }

    [Fact]
    public void The_same_item_announced_again_at_a_list_boundary_is_dropped()
    {
        // Arrowing past the end re-raises the event for an item that never
        // moved. Silence is how the user learns they are at the boundary.
        var (a, _) = Make();
        a.Evaluate(Req(SpeechReason.SelectionChanged), "last", "Mouse").Should().Be(OutputDecision.Speak);
        a.Evaluate(Req(SpeechReason.SelectionChanged), "last", "Mouse").Should().Be(OutputDecision.Drop);
    }

    [Fact]
    public void Moving_to_a_genuinely_different_item_still_speaks()
    {
        var (a, _) = Make();
        a.Evaluate(Req(SpeechReason.SelectionChanged), "a", "General").Should().Be(OutputDecision.Speak);
        a.Evaluate(Req(SpeechReason.SelectionChanged), "b", "Speech").Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void A_user_request_is_never_suppressed_even_if_it_repeats()
    {
        // Pressing "read current line" twice must say the line twice. The user
        // asked, twice, on purpose.
        var (a, _) = Make();
        a.Evaluate(Req(SpeechReason.ReadLine), "doc", "hello").Should().Be(OutputDecision.Speak);
        a.Evaluate(Req(SpeechReason.ReadLine), "doc", "hello").Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void A_user_request_is_not_dropped_by_a_preceding_navigation()
    {
        var (a, _) = Make();
        a.Evaluate(Req(SpeechReason.FocusChanged), "n", "Button").Should().Be(OutputDecision.Speak);
        a.Evaluate(Req(SpeechReason.ReadLine), "n", "Button").Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void Ambient_output_never_suppresses_navigation()
    {
        // A toast arriving mid-navigation must not eat the focus announcement.
        var (a, _) = Make();
        a.Evaluate(Req(SpeechReason.LiveRegionUpdate), "n", "saved").Should().Be(OutputDecision.Speak);
        a.Evaluate(Req(SpeechReason.FocusChanged), "n", "OK button").Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void Navigation_supersedes_a_state_change_for_the_same_control()
    {
        var (a, _) = Make();
        a.Evaluate(Req(SpeechReason.FocusChanged), "cb", "Enabled, check box").Should().Be(OutputDecision.Speak);
        a.Evaluate(Req(SpeechReason.ValueChanged), "cb", "on").Should().Be(OutputDecision.Drop);
    }

    [Fact]
    public void A_state_change_long_after_the_move_is_still_announced()
    {
        // Checking a box a second after landing on it is a real event, not a
        // duplicate of the focus announcement.
        var (a, time) = Make();
        a.Evaluate(Req(SpeechReason.FocusChanged), "cb", "Enabled, check box").Should().Be(OutputDecision.Speak);
        time.Advance(TimeSpan.FromSeconds(1));
        a.Evaluate(Req(SpeechReason.ValueChanged), "cb", "checked").Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void Echo_outranks_navigation_so_typing_feedback_is_never_starved()
    {
        var (a, _) = Make();
        a.Evaluate(Req(SpeechReason.CaretMoved), "edit", "a").Should().Be(OutputDecision.Speak);
        a.Evaluate(new SpeechRequest(SpeechReason.UserAnnouncement, null, "b", null), "edit", "b")
            .Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void Reset_lets_a_control_announce_again()
    {
        var (a, _) = Make();
        a.Evaluate(Req(SpeechReason.SelectionChanged), "x", "Same").Should().Be(OutputDecision.Speak);
        a.Reset();
        a.Evaluate(Req(SpeechReason.SelectionChanged), "x", "Same").Should().Be(OutputDecision.Speak);
    }

    [Fact]
    public void Announcements_about_different_subjects_never_suppress_each_other()
    {
        var (a, _) = Make();
        a.Evaluate(Req(SpeechReason.FocusChanged), "a", "One").Should().Be(OutputDecision.Speak);
        a.Evaluate(Req(SpeechReason.ValueChanged), "b", "Two").Should().Be(OutputDecision.Speak);
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
