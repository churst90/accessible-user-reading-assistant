using FluentAssertions;
using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Navigation;
using OpenReader.Core.Navigation;
using Xunit;

namespace OpenReader.Core.Tests.Navigation;

public class ModeManagerTests
{
    private static AccessibleNode Node(
        string id,
        AccessibleRole role = AccessibleRole.Paragraph,
        AccessibleStates states = AccessibleStates.None)
        => new(new NodeId(id), role, "n", null, null, states, null);

    /// <summary>Everything is a readable document, so the policy always has an opinion.</summary>
    private static ModeManager InDocument()
        => new(new DefaultModePolicy(_ => true));

    [Fact]
    public void The_starting_mode_is_type()
    {
        // Starting in Read mode would swallow keystrokes before the reader has
        // learned anything about where focus is.
        InDocument().Mode.Should().Be(ReaderMode.Type);
    }

    [Fact]
    public void Landing_on_document_text_switches_to_read()
    {
        var m = InDocument();
        m.OnFocusChanged(Node("para", AccessibleRole.Paragraph)).Should().Be(ReaderMode.Read);
    }

    [Fact]
    public void Landing_on_an_edit_field_switches_to_type()
    {
        var m = InDocument();
        m.OnFocusChanged(Node("para", AccessibleRole.Paragraph));
        m.OnFocusChanged(Node("search", AccessibleRole.Edit)).Should().Be(ReaderMode.Type);
    }

    [Fact]
    public void A_read_only_edit_is_for_reading_not_typing()
    {
        var m = InDocument();
        m.OnFocusChanged(Node("log", AccessibleRole.Edit, AccessibleStates.ReadOnly))
            .Should().Be(ReaderMode.Read);
    }

    [Fact]
    public void Leaving_a_readable_document_returns_to_type()
    {
        // A plain dialog has nothing to read; staying in Read mode there would
        // eat the user's keystrokes with no way to explain why.
        var m = new ModeManager(new DefaultModePolicy(n => n.Id.Value.StartsWith("web", StringComparison.Ordinal)));
        m.OnFocusChanged(Node("web-para", AccessibleRole.Paragraph)).Should().Be(ReaderMode.Read);
        m.OnFocusChanged(Node("dialog-button", AccessibleRole.Button)).Should().Be(ReaderMode.Type);
    }

    [Fact]
    public void Toggling_switches_and_reports_that_the_user_asked()
    {
        var m = InDocument();
        var seen = new List<(ReaderMode Mode, bool ByUser)>();
        m.ModeChanged += (mode, byUser) => seen.Add((mode, byUser));

        m.Toggle(Node("para")).Should().Be(ReaderMode.Read);

        seen.Should().ContainSingle();
        seen[0].Should().Be((ReaderMode.Read, true));
    }

    [Fact]
    public void A_manual_override_survives_focus_events_on_the_same_control()
    {
        // Proof-reading a text box in Read mode is a real workflow. Being
        // yanked back to Type mode on every focus event is what makes
        // automatic switching feel hostile.
        var m = InDocument();
        var edit = Node("editor", AccessibleRole.Edit);
        m.OnFocusChanged(edit).Should().Be(ReaderMode.Type);

        m.Toggle(edit).Should().Be(ReaderMode.Read);
        m.OnFocusChanged(edit).Should().Be(ReaderMode.Read);
        m.OnFocusChanged(edit).Should().Be(ReaderMode.Read);
    }

    [Fact]
    public void A_manual_override_does_not_follow_the_user_to_another_control()
    {
        var m = InDocument();
        var edit = Node("editor", AccessibleRole.Edit);
        m.OnFocusChanged(edit);
        m.Toggle(edit).Should().Be(ReaderMode.Read);

        m.OnFocusChanged(Node("other", AccessibleRole.Edit)).Should().Be(ReaderMode.Type);
    }

    [Fact]
    public void Unchanged_mode_raises_no_event()
    {
        // A policy that answers every question would fire constantly and the
        // host would announce the mode on every focus change.
        var m = InDocument();
        m.OnFocusChanged(Node("a", AccessibleRole.Paragraph));

        var events = 0;
        m.ModeChanged += (_, _) => events++;
        m.OnFocusChanged(Node("b", AccessibleRole.Paragraph));
        m.OnFocusChanged(Node("c", AccessibleRole.Paragraph));

        events.Should().Be(0);
    }

    [Fact]
    public void Null_focus_leaves_the_mode_alone()
    {
        var m = InDocument();
        m.OnFocusChanged(Node("para", AccessibleRole.Paragraph));
        m.OnFocusChanged(null).Should().Be(ReaderMode.Read);
    }

    [Fact]
    public void Set_forces_a_mode_and_clears_any_override()
    {
        var m = InDocument();
        var edit = Node("editor", AccessibleRole.Edit);
        m.OnFocusChanged(edit);
        m.Toggle(edit);

        m.Set(ReaderMode.Type);

        m.Mode.Should().Be(ReaderMode.Type);
        m.OnFocusChanged(edit).Should().Be(ReaderMode.Type);
    }

    [Fact]
    public void Toggling_back_and_forth_is_stable()
    {
        var m = InDocument();
        var node = Node("para");
        m.Toggle(node).Should().Be(ReaderMode.Read);
        m.Toggle(node).Should().Be(ReaderMode.Type);
        m.Toggle(node).Should().Be(ReaderMode.Read);
    }
}
