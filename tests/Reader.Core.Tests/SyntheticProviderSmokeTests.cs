using FluentAssertions;
using OpenReader.Abstractions.Accessibility;
using OpenReader.TestKit;
using Xunit;

namespace OpenReader.Core.Tests;

public class SyntheticProviderSmokeTests
{
    [Fact]
    public void SimulateFocus_dispatches_event_to_subscribers()
    {
        var nodes = new SyntheticTreeBuilder()
            .Window("Notepad", w => w
                .MenuBar(m => m
                    .Menu("File", f => f
                        .MenuItem("Open"))))
            .Build();

        using var provider = new SyntheticAccessibilityProvider(nodes);

        AccessibilityEvent? received = null;
        using var sub = provider.Subscribe(AccessibilityEventKind.FocusChanged, e => received = e);

        provider.SimulateFocus("Open");

        received.Should().NotBeNull();
        received!.Kind.Should().Be(AccessibilityEventKind.FocusChanged);
        received.Node.Should().NotBeNull();
        received.Node!.Name.Should().Be("Open");
        received.Node.Role.Should().Be(AccessibleRole.MenuItem);
    }

    [Fact]
    public void Tree_root_resolves_to_top_level_window()
    {
        var nodes = new SyntheticTreeBuilder()
            .Window("App", w => w.Button("OK"))
            .Build();

        using var provider = new SyntheticAccessibilityProvider(nodes);

        provider.Root.Should().NotBeNull();
        provider.Root!.Role.Should().Be(AccessibleRole.Window);
        provider.Root.ChildrenFactory().Should().HaveCount(1);
        provider.Root.ChildrenFactory()[0].Name.Should().Be("OK");
    }
}
