using FluentAssertions;
using Aura.Scripting;
using Xunit;

namespace Aura.Scripting.Tests;

public class PluginApiTests
{
    [Fact]
    public void Same_major_same_minor_is_compatible()
    {
        PluginApi.IsCompatible(PluginApi.CurrentApiVersion).Should().BeTrue();
    }

    [Fact]
    public void Same_major_lower_minor_is_compatible()
    {
        PluginApi.IsCompatible(new System.Version(PluginApi.CurrentApiVersion.Major, 0))
            .Should().BeTrue();
    }

    [Fact]
    public void Same_major_higher_minor_is_refused()
    {
        var future = new System.Version(PluginApi.CurrentApiVersion.Major, PluginApi.CurrentApiVersion.Minor + 1);
        PluginApi.IsCompatible(future).Should().BeFalse();
    }

    [Fact]
    public void Different_major_is_refused()
    {
        PluginApi.IsCompatible(new System.Version(PluginApi.CurrentApiVersion.Major + 1, 0))
            .Should().BeFalse();
    }
}
