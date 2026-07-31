using FluentAssertions;
using OpenReader.Input.Gestures;
using Xunit;

namespace OpenReader.Input.Tests;

public class DoubleTapDetectorTests
{
    [Fact]
    public void First_press_returns_false()
    {
        var d = new DoubleTapDetector(TimeSpan.FromSeconds(1));
        d.Observe(42).Should().BeFalse();
    }

    [Fact]
    public void Second_press_within_window_returns_true()
    {
        var d = new DoubleTapDetector(TimeSpan.FromSeconds(1));
        d.Observe(42);
        d.Observe(42).Should().BeTrue();
    }

    [Fact]
    public void Third_press_after_double_starts_a_new_pair()
    {
        var d = new DoubleTapDetector(TimeSpan.FromSeconds(1));
        d.Observe(42);
        d.Observe(42).Should().BeTrue();
        d.Observe(42).Should().BeFalse();
    }

    [Fact]
    public void Different_keys_do_not_interfere()
    {
        var d = new DoubleTapDetector(TimeSpan.FromSeconds(1));
        d.Observe(1);
        d.Observe(2).Should().BeFalse();
        d.Observe(1).Should().BeTrue();
    }

    [Fact]
    public async Task Press_outside_window_returns_false()
    {
        var d = new DoubleTapDetector(TimeSpan.FromMilliseconds(50));
        d.Observe(7);
        await Task.Delay(120);
        d.Observe(7).Should().BeFalse();
    }
}
