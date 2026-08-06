using Aura.Abstractions.Accessibility;
using Aura.Core.Output;
using FluentAssertions;
using Xunit;

namespace Aura.Core.Tests.Output;

/// <summary>
/// The staleness rules, stated once.
/// </summary>
/// <remarks>
/// Every case here is a bug that was previously chased with a timer. The
/// question "is this announcement still wanted?" is about what has focus, not
/// about how long ago the announcement was composed, and each test below is a
/// place where the timing-based answer got it wrong in one direction or the
/// other.
/// </remarks>
public class FocusTrackerTests
{
    private static AccessibleNode Node(string id) => new(
        new NodeId(id), AccessibleRole.Button, id, null, null,
        AccessibleStates.None, null, () => [], new Dictionary<string, object?>());

    [Fact]
    public void An_announcement_about_the_current_focus_is_valid()
    {
        var t = new FocusTracker();
        t.OnFocusChanged(Node("a"));

        t.For("a").IsStillValid().Should().BeTrue();
    }

    [Fact]
    public void An_announcement_about_a_control_that_has_lost_focus_is_stale()
    {
        // Arrowing through a folder: the announcement for the item just left
        // must evaporate, or speech runs an item behind the cursor.
        var t = new FocusTracker();
        t.OnFocusChanged(Node("a"));
        var stale = t.For("a");
        t.OnFocusChanged(Node("b"));

        stale.IsStillValid().Should().BeFalse();
    }

    [Fact]
    public void Something_that_never_held_focus_is_still_stale_once_focus_is_elsewhere()
    {
        // There was an exemption here for anything the tracker had never seen
        // hold focus, meant to keep alerts and toasts audible. It was
        // unnecessary — those announcements are never given a predicate at all,
        // only focus and selection are — and it was harmful: a focus event the
        // provider deduped away never reached the tracker, so a stale
        // announcement about that element looked unfamiliar and was let
        // through. Pressing Down in a list then read the item you had just
        // left, followed by the one you were on.
        var t = new FocusTracker();
        t.OnFocusChanged(Node("a"));
        var strayed = t.For("never-seen");
        t.OnFocusChanged(Node("b"));

        strayed.IsStillValid().Should().BeFalse();
    }

    // What the exemption was reaching for — an alert surviving a focus change —
    // is covered end to end by the an-alert-survives-a-focus-change transcript,
    // because it depends on alerts never being given a predicate at all, which
    // is a fact about the pipeline rather than about this class.

    [Fact]
    public void An_ancestor_of_the_new_focus_is_still_valid()
    {
        // "Dialog, Save" — focus moves from the dialog into a control inside
        // it, and the user wants both halves. The container announcement is
        // telling them where they are, which is not stale.
        var t = new FocusTracker();
        t.OnFocusChanged(Node("dialog"));
        var container = t.For("dialog");
        t.OnFocusChanged(Node("save-button"), ancestorIds: ["dialog"]);

        container.IsStillValid().Should().BeTrue();
    }

    [Fact]
    public void The_owning_window_is_still_valid()
    {
        // Win+R: the Run dialog's title must be heard even though focus lands
        // on the edit inside it.
        var t = new FocusTracker();
        t.OnFocusChanged(Node("run-window"));
        var title = t.For("run-window");
        t.OnFocusChanged(Node("edit"), windowId: "run-window");

        title.IsStillValid().Should().BeTrue();
    }

    [Fact]
    public void Returning_to_a_control_makes_its_announcement_valid_again()
    {
        var t = new FocusTracker();
        t.OnFocusChanged(Node("a"));
        t.OnFocusChanged(Node("b"));
        var back = t.For("a");
        t.OnFocusChanged(Node("a"));

        back.IsStillValid().Should().BeTrue();
    }

    [Fact]
    public void An_announcement_with_no_subject_is_unconditionally_valid()
    {
        // Anything the user pressed a key to hear. Silence in answer to a
        // keystroke is never the right behaviour.
        var t = new FocusTracker();
        t.OnFocusChanged(Node("a"));
        var anonymous = t.For(null);
        t.OnFocusChanged(Node("b"));

        anonymous.IsStillValid().Should().BeTrue();
    }
}
