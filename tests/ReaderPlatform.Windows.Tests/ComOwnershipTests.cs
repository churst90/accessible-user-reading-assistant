using FluentAssertions;
using Aura.Platform.Windows.Interop;
using Xunit;

namespace Aura.Platform.Windows.Tests;

/// <summary>
/// The accounting behind "no UIA reference is reachable from a finalizer".
/// </summary>
/// <remarks>
/// <para>
/// These drive <see cref="ComReleaseQueue"/> through its injected release
/// action rather than through COM, because what can actually be wrong here is
/// the bookkeeping: released twice, released never, released early, released on
/// the wrong side of a transfer. None of that needs a real element to get
/// wrong, and none of it needs one to catch.
/// </para>
/// <para>
/// What they deliberately do not prove is the thing that made this worth
/// building — that release happens on the dispatch thread rather than the
/// finalizer's. That is a property of where <c>Drain</c> is called from, and it
/// is asserted by the one call site in the dispatch loop.
/// </para>
/// </remarks>
public class ComOwnershipTests
{
    /// <summary>A stand-in for a COM reference: it only has to be an object.</summary>
    private sealed class FakeElement
    {
        public string Name { get; init; } = "element";
    }

    private static (ComReleaseQueue Queue, List<object> Released) NewQueue()
    {
        var released = new List<object>();
        var queue = new ComReleaseQueue(o => { released.Add(o); return 0; });
        return (queue, released);
    }

    [Fact]
    public void Disposing_does_not_release_until_the_queue_is_drained()
    {
        var (queue, released) = NewQueue();
        var owned = queue.Own(new FakeElement());

        owned.Dispose();

        // The whole point: disposal defers. If this ever releases inline, it is
        // releasing on whatever thread happened to call Dispose.
        released.Should().BeEmpty();
        queue.PendingCount.Should().Be(1);
        queue.LiveCount.Should().Be(1);

        queue.Drain().Should().Be(1);
        released.Should().HaveCount(1);
        queue.LiveCount.Should().Be(0);
        queue.ReleasedCount.Should().Be(1);
    }

    [Fact]
    public void A_reference_is_released_exactly_once_however_many_times_it_is_disposed()
    {
        var (queue, released) = NewQueue();
        var owned = queue.Own(new FakeElement());

        owned.Dispose();
        owned.Dispose();
        owned.Dispose();
        queue.Drain();

        // The focus element is both the remembered focus and the cache entry,
        // so eviction disposes the same owner twice by design.
        released.Should().HaveCount(1);
        queue.LiveCount.Should().Be(0);
    }

    [Fact]
    public void Transfer_moves_the_reference_and_leaves_the_original_empty()
    {
        var (queue, released) = NewQueue();
        var element = new FakeElement();
        var first = queue.Own(element);

        var second = first.Transfer();

        first.IsAlive.Should().BeFalse();
        second.IsAlive.Should().BeTrue();
        second.Value.Should().BeSameAs(element);
        // One reference, one owner — the count does not move on a transfer.
        queue.LiveCount.Should().Be(1);
    }

    [Fact]
    public void Disposing_the_owner_a_reference_was_moved_out_of_releases_nothing()
    {
        var (queue, released) = NewQueue();
        var first = queue.Own(new FakeElement());
        var second = first.Transfer();

        first.Dispose();
        queue.Drain();

        // If this released, a focus element would be handed back the moment the
        // event that carried it finished — while the caret path was still
        // reading through it.
        released.Should().BeEmpty();
        queue.LiveCount.Should().Be(1);

        second.Dispose();
        queue.Drain();
        released.Should().HaveCount(1);
        queue.LiveCount.Should().Be(0);
    }

    [Fact]
    public void Reading_a_disposed_owner_fails_loudly()
    {
        var (queue, _) = NewQueue();
        var owned = queue.Own(new FakeElement());
        owned.Dispose();

        owned.Invoking(o => o.Value).Should().Throw<ObjectDisposedException>();
        owned.ValueOrNull.Should().BeNull();
        owned.IsAlive.Should().BeFalse();
    }

    [Fact]
    public void A_release_that_throws_still_clears_the_reference()
    {
        var queue = new ComReleaseQueue(_ => throw new System.Runtime.InteropServices.InvalidComObjectException());
        queue.Own(new FakeElement()).Dispose();

        // A provider that fails on release is one we can do nothing about. The
        // alternative to swallowing it is tearing down the loop that keeps the
        // reader speaking.
        queue.Drain().Should().Be(1);
        queue.LiveCount.Should().Be(0);
    }

    [Fact]
    public void Draining_an_empty_queue_is_free_and_harmless()
    {
        var (queue, released) = NewQueue();

        queue.Drain().Should().Be(0);
        released.Should().BeEmpty();

        // The dispatch loop drains after every event, most of which own nothing.
        queue.LiveCount.Should().Be(0);
    }

    [Fact]
    public void OwnIfNotNull_counts_nothing_for_a_reference_that_never_arrived()
    {
        var (queue, _) = NewQueue();

        queue.OwnIfNotNull<FakeElement>(null).Should().BeNull();
        queue.LiveCount.Should().Be(0);
    }

    [Fact]
    public void Every_owned_reference_is_accounted_for_across_many_events()
    {
        var (queue, released) = NewQueue();

        // Roughly what arrowing a folder of fifty icons costs: one focus
        // element plus a parent and every sibling, per press.
        for (var press = 0; press < 20; press++)
        {
            var focus = queue.Own(new FakeElement());
            using (var parent = queue.Own(new FakeElement()))
            {
                for (var sibling = 0; sibling < 50; sibling++)
                {
                    queue.Own(new FakeElement()).Dispose();
                }
            }
            focus.Dispose();
            queue.Drain();

            // Between events the count returns to zero. A count that crept up
            // by one per press is the leak this whole mechanism is for.
            queue.LiveCount.Should().Be(0);
        }

        released.Should().HaveCount(20 * 52);
    }
}
