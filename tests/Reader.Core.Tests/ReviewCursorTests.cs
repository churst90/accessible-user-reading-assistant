using FluentAssertions;
using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Text;
using OpenReader.Core.Review;
using Xunit;

namespace OpenReader.Core.Tests;

public class ReviewCursorTests
{
    private sealed class SingleSurfaceProvider : ITextSurfaceProvider
    {
        private readonly ITextSurface? _surface;
        public SingleSurfaceProvider(ITextSurface? surface) => _surface = surface;
        public ITextSurface? GetSurface(AccessibleNode node) => _surface;
    }

    private static AccessibleNode Node(string id = "n1")
        => new(new NodeId(id), AccessibleRole.Document, "doc", null, null, AccessibleStates.None, null);

    private static ReviewCursor CursorOver(string text, int caret = 0)
        => new(new SingleSurfaceProvider(new StringTextSurface(text, caret)));

    [Fact]
    public void Sync_binds_and_reads_from_the_caret()
    {
        var c = CursorOver("hello world");
        c.SyncTo(Node());

        c.HasText.Should().BeTrue();
        c.ReadCurrentCharacter().Should().Be("h");
        c.ReadCurrentWord().Should().Be("hello");
        c.ReadCurrentLine().Should().Be("hello world");
    }

    [Fact]
    public void Sync_starts_where_the_user_already_is()
    {
        // Not at offset zero: resetting to the top would make the user
        // re-navigate to the place they were already looking at.
        var c = CursorOver("hello world", caret: 6);
        c.SyncTo(Node());

        c.ReadCurrentWord().Should().Be("world");
    }

    [Fact]
    public void Character_navigation_walks_forward_and_back()
    {
        var c = CursorOver("abc");
        c.SyncTo(Node());

        c.MoveNextCharacter().Should().Be("b");
        c.MoveNextCharacter().Should().Be("c");
        c.MoveNextCharacter().Should().BeEmpty();
        c.MovePreviousCharacter().Should().Be("b");
    }

    [Fact]
    public void Word_navigation_walks_forward_and_back()
    {
        var c = CursorOver("alpha beta gamma");
        c.SyncTo(Node());

        c.MoveNextWord().Should().Be("beta");
        c.MoveNextWord().Should().Be("gamma");
        c.MoveNextWord().Should().BeEmpty();
        c.MovePreviousWord().Should().Be("beta");
    }

    [Fact]
    public void Line_navigation_walks_forward_and_back()
    {
        var c = CursorOver("one\ntwo\nthree");
        c.SyncTo(Node());

        c.ReadCurrentLine().Should().Be("one");
        c.MoveNextLine().Should().Be("two");
        c.MoveNextLine().Should().Be("three");
        c.MoveNextLine().Should().BeEmpty();
        c.MovePreviousLine().Should().Be("two");
    }

    [Fact]
    public void Stepping_right_off_the_last_character_does_not_strand_the_cursor()
    {
        // If the cursor advanced onto the document's end position it would
        // read empty, and the user would have to press "previous" twice to get
        // back to the last character.
        var c = CursorOver("abc");
        c.SyncTo(Node());
        c.MoveNextCharacter();
        c.MoveNextCharacter(); // on "c"

        c.MoveNextCharacter().Should().BeEmpty();
        c.ReadCurrentCharacter().Should().Be("c");
        c.MovePreviousCharacter().Should().Be("b");
    }

    [Fact]
    public void Blank_lines_inside_a_document_stay_traversable()
    {
        // An empty reading mid-document is a blank line, not the end. Refusing
        // to move onto it would make a blank line an impassable wall.
        var c = CursorOver("alpha\n\nbravo");
        c.SyncTo(Node());
        c.ReadCurrentLine().Should().Be("alpha");

        c.MoveNextLine().Should().BeEmpty();       // the blank line
        c.MoveNextLine().Should().Be("bravo");     // and past it
    }

    [Fact]
    public void Move_to_start_and_end_jump_to_the_document_bounds()
    {
        var c = CursorOver("one\ntwo\nthree", caret: 5);
        c.SyncTo(Node());

        c.MoveToEnd();
        c.ReadCurrentLine().Should().Be("three");

        c.MoveToStart();
        c.ReadCurrentLine().Should().Be("one");
    }

    [Fact]
    public void Review_follows_the_caret_on_demand()
    {
        // Roadmap 3.6 #3. Review and the caret are the same kind of thing over
        // the same surface, so this is a position copy rather than a feature.
        var surface = new StringTextSurface("one\ntwo\nthree", caretOffset: 0);
        var c = new ReviewCursor(new SingleSurfaceProvider(surface));
        c.SyncTo(Node());
        c.ReadCurrentLine().Should().Be("one");

        surface.CaretOffset = 9; // inside "three"
        c.FollowCaret().Should().BeTrue();
        c.ReadCurrentLine().Should().Be("three");
    }

    [Fact]
    public void Edits_are_visible_without_an_explicit_refresh()
    {
        // The old snapshot-based cursor read deleted content until the next
        // focus change; Refresh existed only to paper over that.
        var surface = new StringTextSurface("original text", caretOffset: 0);
        var c = new ReviewCursor(new SingleSurfaceProvider(surface));
        c.SyncTo(Node());
        c.ReadCurrentLine().Should().Be("original text");

        surface.Text = "replaced text";
        c.ReadCurrentLine().Should().Be("replaced text");
    }

    [Fact]
    public void A_node_with_no_text_surface_reads_empty_rather_than_throwing()
    {
        var c = new ReviewCursor(new SingleSurfaceProvider(null));
        c.SyncTo(Node());

        c.HasText.Should().BeFalse();
        c.ReadCurrentLine().Should().BeEmpty();
        c.ReadCurrentWord().Should().BeEmpty();
        c.MoveNextLine().Should().BeEmpty();
    }

    [Fact]
    public void Reading_is_capped_so_one_keystroke_cannot_speak_a_whole_minified_file()
    {
        var c = CursorOver(new string('x', 50_000));
        c.SyncTo(Node());

        c.ReadCurrentLine().Length.Should().Be(8192);
    }
}
