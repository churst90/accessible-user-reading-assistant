using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Speech;
using Aura.Core.Output;
using FluentAssertions;
using Xunit;

namespace Aura.Core.Tests.Output;

/// <summary>
/// Which announcements can go stale, and which must never be reconsidered.
/// </summary>
/// <remarks>
/// This policy lived in the host, uncovered, with a copy of it in the
/// transcript harness. The copies drifted: the host asked a focus question
/// about selection announcements, every one was swept, and lists went silent —
/// and the harness asked the same wrong question and agreed. These are the
/// tests that were impossible while it was wiring.
/// </remarks>
public class AnnouncementPolicyTests
{
    private static AccessibleNode Node(string id) => new(
        new NodeId(id), AccessibleRole.ListItem, id, null, null,
        AccessibleStates.None, null);

    private static SpeechRequest Request(SpeechReason reason, string id) =>
        new(reason, Node(id), null, null);

    [Fact]
    public void A_focus_announcement_goes_stale_when_focus_moves_on()
    {
        var policy = new AnnouncementPolicy();
        policy.OnFocusChanged(Node("a"), () => 0);
        var stale = policy.ValidityFor(Request(SpeechReason.FocusChanged, "a"));

        policy.OnFocusChanged(Node("b"), () => 0);

        stale!.IsStillValid().Should().BeFalse();
    }

    [Fact]
    public void A_selection_announcement_is_never_asked_about_focus()
    {
        // A list box keeps keyboard focus on the LIST while the arrows move the
        // selection, so the item being announced is never the focused element.
        // Asking made every list in the settings dialog silent.
        var policy = new AnnouncementPolicy();
        policy.OnFocusChanged(Node("the-list"), () => 0);

        policy.ValidityFor(Request(SpeechReason.SelectionChanged, "an-item")).Should().BeNull();
    }

    [Theory]
    [InlineData(SpeechReason.AlertRaised)]
    [InlineData(SpeechReason.LiveRegionUpdate)]
    [InlineData(SpeechReason.ToolTipOpened)]
    [InlineData(SpeechReason.UserAnnouncement)]
    [InlineData(SpeechReason.ReadLine)]
    [InlineData(SpeechReason.CaretMoved)]
    public void Nothing_else_carries_a_predicate(SpeechReason reason)
    {
        // An alert fires on something that by definition does not have focus,
        // and silence in answer to a keystroke is never right.
        var policy = new AnnouncementPolicy();
        policy.OnFocusChanged(Node("a"), () => 0);

        policy.ValidityFor(Request(reason, "whatever")).Should().BeNull();
    }

    [Fact]
    public void The_sweep_runs_after_the_new_focus_is_recorded()
    {
        // The entire subtlety. A predicate evaluated before the new focus is
        // known answers about a world that no longer exists, so nothing is
        // dropped and the user hears the item they just left.
        var policy = new AnnouncementPolicy();
        policy.OnFocusChanged(Node("a"), () => 0);
        var leaving = policy.ValidityFor(Request(SpeechReason.FocusChanged, "a"))!;

        var validAtSweepTime = true;
        policy.OnFocusChanged(Node("b"), () =>
        {
            validAtSweepTime = leaving.IsStillValid();
            return 0;
        });

        validAtSweepTime.Should().BeFalse();
    }
}
