using FluentAssertions;
using OpenReader.Abstractions.Accessibility;
using Xunit;

namespace OpenReader.Abstractions.Tests;

public class AccessibleNodeTests
{
    [Fact]
    public void HasState_returns_true_when_state_set()
    {
        var node = new AccessibleNode(
            id: new NodeId("n1"),
            role: AccessibleRole.CheckBox,
            name: "Subscribe",
            value: null,
            description: null,
            states: AccessibleStates.Focusable | AccessibleStates.Checked,
            parentId: null);

        node.HasState(AccessibleStates.Checked).Should().BeTrue();
        node.HasState(AccessibleStates.Focusable).Should().BeTrue();
        node.HasState(AccessibleStates.Disabled).Should().BeFalse();
    }

    [Fact]
    public void Empty_NodeId_round_trips()
    {
        NodeId.Empty.IsEmpty.Should().BeTrue();
        new NodeId("anything").IsEmpty.Should().BeFalse();
    }
}
