using FluentAssertions;
using Aura.Abstractions.Text;
using Aura.Core.Text;
using Xunit;

namespace Aura.Core.Tests.Text;

/// <summary>
/// Position-diffing replaces classifying the keystroke. Each test below is a
/// case the keystroke-classification path in <c>CaretLineTracker</c> gets
/// wrong or cannot see at all.
/// </summary>
public class CaretMotionResolverTests
{
    /// <summary>Snapshot the caret, move it, snapshot again — what the reader actually observes.</summary>
    private static (ITextRange Before, ITextRange After) Move(StringTextSurface s, int to)
    {
        var before = s.GetCaret()!;
        s.CaretOffset = to;
        return (before, s.GetCaret()!);
    }

    [Fact]
    public void Stepping_one_character_right_reads_the_character_at_the_caret()
    {
        var s = new StringTextSurface("abc", caretOffset: 0);
        var (before, after) = Move(s, 1);

        var motion = CaretMotionResolver.Resolve(before, after);
        motion.Kind.Should().Be(CaretMotionKind.Character);
        motion.Text.Should().Be("b");
    }

    [Fact]
    public void Stepping_one_character_left_reads_the_character_at_the_caret()
    {
        var s = new StringTextSurface("abc", caretOffset: 2);
        var (before, after) = Move(s, 1);

        var motion = CaretMotionResolver.Resolve(before, after);
        motion.Kind.Should().Be(CaretMotionKind.Character);
        motion.Text.Should().Be("b");
    }

    [Fact]
    public void With_no_key_behind_it_a_wrap_is_inferred_as_a_line_move()
    {
        // No requested unit: a mouse click, a find result, or a caret event
        // with no keystroke behind it. Nobody asked for a granularity, so the
        // distance covered is the best available answer — and it crossed a
        // line, so read the line.
        var s = new StringTextSurface("ab\ncd", caretOffset: 3);
        var (before, after) = Move(s, 2);

        var motion = CaretMotionResolver.Resolve(before, after);
        motion.Kind.Should().Be(CaretMotionKind.Line);
        motion.Text.Should().Be("ab");
    }

    [Fact]
    public void Left_arrow_wrapping_to_the_previous_line_still_reports_a_character()
    {
        // The same movement, with the key's granularity supplied — and this is
        // the case that inference gets wrong. The user pressed Left once and
        // asked for one character. Reading the whole previous line back to them
        // because a newline happened to be crossed answers a one-character
        // request with a paragraph, and it is what Cody heard on hardware.
        var s = new StringTextSurface("ab\ncd", caretOffset: 3);
        var (before, after) = Move(s, 2);

        var motion = CaretMotionResolver.Resolve(before, after, TextUnit.Character);
        motion.Kind.Should().Be(CaretMotionKind.Character);
        motion.Text.Should().NotBe("ab");
    }

    [Fact]
    public void A_requested_line_move_still_reads_the_line()
    {
        var s = new StringTextSurface("first\nsecond", caretOffset: 2);
        var (before, after) = Move(s, 8);

        var motion = CaretMotionResolver.Resolve(before, after, TextUnit.Line);
        motion.Kind.Should().Be(CaretMotionKind.Line);
        motion.Text.Should().Be("second");
    }

    [Fact]
    public void A_requested_word_move_reads_the_word_even_when_it_crossed_a_line()
    {
        var s = new StringTextSurface("alpha\nbravo charlie", caretOffset: 0);
        var (before, after) = Move(s, 6);

        var motion = CaretMotionResolver.Resolve(before, after, TextUnit.Word);
        motion.Kind.Should().Be(CaretMotionKind.Word);
        motion.Text.Should().Be("bravo");
    }

    [Fact]
    public void Down_arrow_reads_the_new_line()
    {
        var s = new StringTextSurface("first\nsecond\nthird", caretOffset: 2);
        var (before, after) = Move(s, 8);

        var motion = CaretMotionResolver.Resolve(before, after);
        motion.Kind.Should().Be(CaretMotionKind.Line);
        motion.Text.Should().Be("second");
    }

    [Fact]
    public void Word_jump_reads_the_word_at_the_caret()
    {
        var s = new StringTextSurface("alpha beta gamma", caretOffset: 0);
        var (before, after) = Move(s, 6);

        var motion = CaretMotionResolver.Resolve(before, after);
        motion.Kind.Should().Be(CaretMotionKind.Word);
        motion.Text.Should().Be("beta");
    }

    [Fact]
    public void Word_jump_keeps_contractions_whole()
    {
        var s = new StringTextSurface("I don't care", caretOffset: 0);
        var (before, after) = Move(s, 2);

        CaretMotionResolver.Resolve(before, after).Text.Should().Be("don't");
    }

    [Fact]
    public void Ctrl_end_across_the_document_is_a_line_move()
    {
        var s = new StringTextSurface("one\ntwo\nthree", caretOffset: 0);
        var (before, after) = Move(s, 13);

        var motion = CaretMotionResolver.Resolve(before, after);
        motion.Kind.Should().Be(CaretMotionKind.Line);
        motion.Text.Should().Be("three");
    }

    [Fact]
    public void An_emoji_is_one_character_not_two_halves()
    {
        // The Win32 fallback currently does line[col].ToString(), which hands
        // half a surrogate pair to the synthesiser.
        var s = new StringTextSurface("a\U0001F600b", caretOffset: 0);
        var (before, after) = Move(s, 1);

        var motion = CaretMotionResolver.Resolve(before, after);
        motion.Kind.Should().Be(CaretMotionKind.Character);
        motion.Text.Should().Be("\U0001F600");
    }

    [Fact]
    public void Stepping_back_over_an_emoji_is_also_one_character()
    {
        var s = new StringTextSurface("a\U0001F600b", caretOffset: 3);
        var (before, after) = Move(s, 1);

        var motion = CaretMotionResolver.Resolve(before, after);
        motion.Kind.Should().Be(CaretMotionKind.Character);
        motion.Text.Should().Be("\U0001F600");
    }

    [Fact]
    public void A_caret_move_with_no_keystroke_at_all_still_resolves()
    {
        // Mouse click, find result, autocomplete, a link followed. There is no
        // key to classify; the current design is structurally blind to these.
        var s = new StringTextSurface("first\nsecond\nthird", caretOffset: 1);
        var (before, after) = Move(s, 15);

        var motion = CaretMotionResolver.Resolve(before, after);
        motion.Kind.Should().Be(CaretMotionKind.Line);
        motion.Text.Should().Be("third");
    }

    [Fact]
    public void No_movement_announces_nothing()
    {
        // No 400 ms same-text timer needed: identical positions are identical.
        var s = new StringTextSurface("abc", caretOffset: 1);
        var (before, after) = Move(s, 1);

        CaretMotionResolver.Resolve(before, after).Kind.Should().Be(CaretMotionKind.None);
    }

    [Fact]
    public void The_first_observation_orients_with_the_whole_line()
    {
        var s = new StringTextSurface("first\nsecond", caretOffset: 8);

        var motion = CaretMotionResolver.Resolve(previous: null, current: s.GetCaret());
        motion.Kind.Should().Be(CaretMotionKind.Line);
        motion.Text.Should().Be("second");
    }

    [Fact]
    public void Losing_the_caret_announces_nothing()
    {
        var s = new StringTextSurface("abc", caretOffset: 1);
        CaretMotionResolver.Resolve(s.GetCaret(), current: null).Kind.Should().Be(CaretMotionKind.None);
    }

    [Fact]
    public void Shift_right_reports_the_character_that_joined_the_selection()
    {
        var s = new StringTextSurface("hello", caretOffset: 0);
        var before = s.GetCaret()!;
        s.Select(anchor: 0, active: 1);

        var motion = CaretMotionResolver.Resolve(before, s.GetSelection());
        motion.Kind.Should().Be(CaretMotionKind.SelectionGrew);
        motion.Text.Should().Be("h");
    }

    [Fact]
    public void Growing_a_selection_reports_only_the_delta()
    {
        var s = new StringTextSurface("hello world");
        s.Select(0, 5);
        var before = s.GetSelection()!;
        s.Select(0, 11);

        var motion = CaretMotionResolver.Resolve(before, s.GetSelection());
        motion.Kind.Should().Be(CaretMotionKind.SelectionGrew);
        motion.Text.Should().Be(" world");
    }

    [Fact]
    public void Shrinking_a_selection_reports_what_left_it()
    {
        var s = new StringTextSurface("hello world");
        s.Select(0, 11);
        var before = s.GetSelection()!;
        s.Select(0, 5);

        var motion = CaretMotionResolver.Resolve(before, s.GetSelection());
        motion.Kind.Should().Be(CaretMotionKind.SelectionShrank);
        motion.Text.Should().Be(" world");
    }

    [Fact]
    public void Dropping_a_selection_reports_what_had_been_selected()
    {
        var s = new StringTextSurface("hello world");
        s.Select(0, 5);
        var before = s.GetSelection()!;
        s.CaretOffset = 5;

        var motion = CaretMotionResolver.Resolve(before, s.GetSelection());
        motion.Kind.Should().Be(CaretMotionKind.SelectionCleared);
        motion.Text.Should().Be("hello");
    }

    [Fact]
    public void An_unchanged_selection_announces_nothing()
    {
        // Shift+End when already at end of line.
        var s = new StringTextSurface("hello world");
        s.Select(0, 11);
        var before = s.GetSelection()!;
        s.Select(0, 11);

        CaretMotionResolver.Resolve(before, s.GetSelection()).Kind.Should().Be(CaretMotionKind.None);
    }

    [Fact]
    public void Arrowing_onto_a_blank_line_says_blank_not_the_line_above()
    {
        var s = new StringTextSurface("alpha\n\nbravo", caretOffset: 2);
        var (before, after) = Move(s, 6); // the empty line

        var motion = CaretMotionResolver.Resolve(before, after);
        motion.Kind.Should().Be(CaretMotionKind.Line);
        motion.Text.Should().BeEmpty();
    }

    [Fact]
    public void A_real_line_below_still_reads_its_own_text()
    {
        var s = new StringTextSurface("alpha\nbravo", caretOffset: 2);
        var (before, after) = Move(s, 8);

        CaretMotionResolver.Resolve(before, after).Text.Should().Be("bravo");
    }

    [Fact]
    public void Text_for_reads_the_unit_a_motion_implies()
    {
        var s = new StringTextSurface("alpha beta", caretOffset: 0);
        var (before, after) = Move(s, 6);

        var motion = CaretMotionResolver.Resolve(before, after);
        CaretMotionResolver.TextFor(motion, after).Should().Be("beta");
    }
}
