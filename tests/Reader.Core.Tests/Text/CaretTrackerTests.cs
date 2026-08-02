using FluentAssertions;
using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Text;
using Aura.Core.Text;
using Xunit;

namespace Aura.Core.Tests.Text;

/// <summary>
/// Caret following, driven end to end without Windows. The behaviour these
/// cover could previously only be checked by running the compiled exe and
/// listening — which is why the roadmap's correctness punch list exists.
/// </summary>
public class CaretTrackerTests
{
    private static AccessibleNode Node(string id = "edit1")
        => new(new NodeId(id), AccessibleRole.Edit, "editor", null, null, AccessibleStates.None, null);

    /// <summary>Hands out one surface per node id, as the real provider must.</summary>
    private sealed class FakeSurfaceProvider : ITextSurfaceProvider
    {
        private readonly Dictionary<string, ITextSurface> _byNode = new(StringComparer.Ordinal);

        public void Set(string nodeId, ITextSurface surface) => _byNode[nodeId] = surface;

        public ITextSurface? GetSurface(AccessibleNode node)
            => _byNode.TryGetValue(node.Id.Value, out var s) ? s : null;
    }

    private sealed class Harness
    {
        public FakeSurfaceProvider Surfaces { get; } = new();
        public List<CaretMotion> Announced { get; } = new();
        public AccessibleNode? Focused { get; set; } = Node();
        public bool Typing { get; set; }
        public CaretTracker Tracker { get; }

        public Harness(StringTextSurface surface, string nodeId = "edit1")
        {
            Surfaces.Set(nodeId, surface);
            Tracker = new CaretTracker(
                Surfaces,
                () => Focused,
                (motion, _) => Announced.Add(motion),
                () => Typing);
        }
    }

    [Fact]
    public void The_first_sample_on_a_control_establishes_a_baseline_silently()
    {
        // Focus already announced where the user is; repeating it is noise.
        var h = new Harness(new StringTextSurface("hello world", caretOffset: 0));

        h.Tracker.Sample().Kind.Should().Be(CaretMotionKind.None);
        h.Announced.Should().BeEmpty();
    }

    [Fact]
    public void Moving_then_sampling_announces_the_character()
    {
        var surface = new StringTextSurface("abc", caretOffset: 0);
        var h = new Harness(surface);
        h.Tracker.Sample(); // baseline

        surface.CaretOffset = 1;
        var motion = h.Tracker.Sample();

        motion.Kind.Should().Be(CaretMotionKind.Character);
        motion.Text.Should().Be("b");
        h.Announced.Should().ContainSingle();
    }

    [Fact]
    public void Sampling_twice_for_one_movement_announces_once()
    {
        // This is what removes the 250 ms cross-component suppression window:
        // a keystroke trigger and a caret-event trigger for the same move are
        // simply two samples, and the second finds nothing changed.
        var surface = new StringTextSurface("abc", caretOffset: 0);
        var h = new Harness(surface);
        h.Tracker.Sample();

        surface.CaretOffset = 1;
        h.Tracker.Sample();
        h.Tracker.Sample();

        h.Announced.Should().ContainSingle();
    }

    [Fact]
    public void Sampling_before_the_app_has_moved_the_caret_stays_silent()
    {
        // The old design read after a fixed 15 ms and announced whatever it
        // found — which under load was the position the user had just left.
        var surface = new StringTextSurface("abc", caretOffset: 0);
        var h = new Harness(surface);
        h.Tracker.Sample();

        h.Tracker.Sample().Kind.Should().Be(CaretMotionKind.None);
        h.Announced.Should().BeEmpty();
    }

    [Fact]
    public async Task Polling_waits_for_a_slow_control_and_stops_as_soon_as_it_moves()
    {
        var surface = new StringTextSurface("abc", caretOffset: 0);
        var h = new Harness(surface);
        h.Tracker.Sample();
        h.Tracker.SettleBudget = TimeSpan.FromMilliseconds(500);
        h.Tracker.PollInterval = TimeSpan.FromMilliseconds(2);

        // The "application" reacts after a delay, as a loaded one would.
        var pending = h.Tracker.SampleUntilChangedAsync();
        await Task.Delay(30);
        surface.CaretOffset = 1;

        var motion = await pending;
        motion.Kind.Should().Be(CaretMotionKind.Character);
        motion.Text.Should().Be("b");
    }

    [Fact]
    public async Task Polling_gives_up_quietly_when_nothing_moves()
    {
        var h = new Harness(new StringTextSurface("abc", caretOffset: 0));
        h.Tracker.Sample();
        h.Tracker.SettleBudget = TimeSpan.FromMilliseconds(20);
        h.Tracker.PollInterval = TimeSpan.FromMilliseconds(2);

        (await h.Tracker.SampleUntilChangedAsync()).Kind.Should().Be(CaretMotionKind.None);
        h.Announced.Should().BeEmpty();
    }

    [Fact]
    public void Typing_suppresses_caret_announcements()
    {
        // Key echo owns typing. Re-reading the growing value is the Run-box bug.
        var surface = new StringTextSurface("a", caretOffset: 1);
        var h = new Harness(surface);
        h.Tracker.Sample();

        h.Typing = true;
        surface.Text = "ab";
        surface.CaretOffset = 2;

        h.Tracker.Sample().Kind.Should().Be(CaretMotionKind.None);
        h.Announced.Should().BeEmpty();
    }

    [Fact]
    public void The_first_move_after_typing_is_not_diffed_against_a_pre_edit_position()
    {
        // Without re-baselining while typing, the caret would appear to have
        // jumped from wherever it was before the edit, and the reader would
        // announce a bogus word or line.
        var surface = new StringTextSurface("hello", caretOffset: 0);
        var h = new Harness(surface);
        h.Tracker.Sample();

        h.Typing = true;
        surface.Text = "hello world";
        surface.CaretOffset = 11;
        h.Tracker.Sample();

        h.Typing = false;
        surface.CaretOffset = 10;
        var motion = h.Tracker.Sample();

        motion.Kind.Should().Be(CaretMotionKind.Character);
        motion.Text.Should().Be("d");
    }

    [Fact]
    public void Focus_moving_to_another_control_does_not_diff_across_them()
    {
        var first = new StringTextSurface("aaaa", caretOffset: 3);
        var second = new StringTextSurface("bbbb", caretOffset: 0);
        var h = new Harness(first);
        h.Surfaces.Set("edit2", second);
        h.Tracker.Sample();

        h.Focused = Node("edit2");
        h.Tracker.Sample().Kind.Should().Be(CaretMotionKind.None);
        h.Announced.Should().BeEmpty();

        // ...and tracking resumes normally in the new control.
        second.CaretOffset = 1;
        h.Tracker.Sample().Kind.Should().Be(CaretMotionKind.Character);
    }

    [Fact]
    public void Reset_drops_the_baseline_so_nothing_is_announced_across_it()
    {
        var surface = new StringTextSurface("abc", caretOffset: 0);
        var h = new Harness(surface);
        h.Tracker.Sample();

        h.Tracker.Reset();
        surface.CaretOffset = 2;

        h.Tracker.Sample().Kind.Should().Be(CaretMotionKind.None);
        h.Announced.Should().BeEmpty();
    }

    [Fact]
    public void A_control_with_no_text_surface_is_silent_rather_than_broken()
    {
        var h = new Harness(new StringTextSurface("x"));
        h.Focused = Node("button-with-no-text");

        h.Tracker.Sample().Kind.Should().Be(CaretMotionKind.None);
        h.Announced.Should().BeEmpty();
    }

    [Fact]
    public void Selection_growth_is_announced_as_a_selection()
    {
        var surface = new StringTextSurface("hello world", caretOffset: 0);
        var h = new Harness(surface);
        h.Tracker.Sample();

        surface.Select(0, 5);
        var motion = h.Tracker.Sample();

        motion.Kind.Should().Be(CaretMotionKind.SelectionGrew);
        motion.Text.Should().Be("hello");
    }
}
