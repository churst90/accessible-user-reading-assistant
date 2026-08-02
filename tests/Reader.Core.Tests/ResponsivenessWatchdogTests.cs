using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Aura.Core.Diagnostics;
using Xunit;

namespace Aura.Core.Tests;

public class ResponsivenessWatchdogTests
{
    private static (ResponsivenessWatchdog Dog, FakeTimeProvider Time, List<TimeSpan> Stalls) Make()
    {
        var time = new FakeTimeProvider();
        var dog = new ResponsivenessWatchdog(time) { StallThreshold = TimeSpan.FromSeconds(2) };
        var stalls = new List<TimeSpan>();
        dog.Stalled += stalls.Add;
        return (dog, time, stalls);
    }

    [Fact]
    public void Silence_with_no_input_is_not_a_stall()
    {
        // The reader is idle most of the time. Idle is not broken.
        var (dog, time, stalls) = Make();

        time.Advance(TimeSpan.FromMinutes(5));
        dog.Poll();

        stalls.Should().BeEmpty();
    }

    [Fact]
    public void Speech_arriving_promptly_is_not_a_stall()
    {
        var (dog, time, stalls) = Make();

        dog.NotifyInput();
        time.Advance(TimeSpan.FromMilliseconds(40));
        dog.NotifyOutput();
        time.Advance(TimeSpan.FromSeconds(10));
        dog.Poll();

        stalls.Should().BeEmpty();
    }

    [Fact]
    public void Input_with_no_speech_past_the_threshold_reports_a_stall()
    {
        var (dog, time, stalls) = Make();

        dog.NotifyInput();
        time.Advance(TimeSpan.FromSeconds(3));
        dog.Poll();

        stalls.Should().ContainSingle();
        stalls[0].Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void A_stall_is_reported_once_not_on_every_poll()
    {
        // Repeating the cue every 500 ms while an app is frozen would itself
        // make the machine unusable.
        var (dog, time, stalls) = Make();

        dog.NotifyInput();
        time.Advance(TimeSpan.FromSeconds(3));
        dog.Poll();
        time.Advance(TimeSpan.FromSeconds(3));
        dog.Poll();
        time.Advance(TimeSpan.FromSeconds(3));
        dog.Poll();

        stalls.Should().ContainSingle();
    }

    [Fact]
    public void A_burst_of_keystrokes_does_not_keep_resetting_the_clock()
    {
        // Holding an arrow key down against a frozen app must still be
        // detected: if each key reset the timer the stall would never surface.
        var (dog, time, stalls) = Make();

        for (var i = 0; i < 20; i++)
        {
            dog.NotifyInput();
            time.Advance(TimeSpan.FromMilliseconds(200));
            dog.Poll();
        }

        stalls.Should().ContainSingle();
    }

    [Fact]
    public void Recovery_is_announced_after_a_reported_stall()
    {
        var (dog, time, _) = Make();
        var recovered = 0;
        dog.Recovered += () => recovered++;

        dog.NotifyInput();
        time.Advance(TimeSpan.FromSeconds(3));
        dog.Poll();
        dog.NotifyOutput();

        recovered.Should().Be(1);
    }

    [Fact]
    public void Recovery_is_not_announced_when_there_was_no_stall()
    {
        var (dog, time, _) = Make();
        var recovered = 0;
        dog.Recovered += () => recovered++;

        dog.NotifyInput();
        time.Advance(TimeSpan.FromMilliseconds(30));
        dog.NotifyOutput();

        recovered.Should().Be(0);
    }

    [Fact]
    public void Polling_after_dispose_is_harmless()
    {
        var (dog, time, stalls) = Make();
        dog.NotifyInput();
        dog.Dispose();

        time.Advance(TimeSpan.FromSeconds(10));
        dog.Poll();

        stalls.Should().BeEmpty();
    }
}
