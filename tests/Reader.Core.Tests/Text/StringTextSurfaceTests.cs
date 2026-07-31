using FluentAssertions;
using OpenReader.Abstractions.Text;
using Xunit;

namespace OpenReader.Core.Tests.Text;

/// <summary>
/// Conformance suite for <see cref="ITextRange"/> semantics. Any other backend
/// — UIA <c>TextPattern</c>, the Win32 <c>EM_*</c> adapter, a browse-mode
/// virtual buffer — is expected to pass an equivalent set. Where a backend
/// disagrees with these, the backend is wrong.
/// </summary>
public class StringTextSurfaceTests
{
    private const string Doc = "first line\nsecond line\nthird line";

    [Fact]
    public void Document_range_covers_everything()
    {
        var s = new StringTextSurface(Doc);
        s.GetDocumentRange().GetText().Should().Be(Doc);
    }

    [Fact]
    public void Caret_range_is_collapsed()
    {
        var s = new StringTextSurface(Doc, caretOffset: 5);
        var caret = s.GetCaret()!;
        caret.IsCollapsed.Should().BeTrue();
        caret.GetText().Should().BeEmpty();
    }

    [Fact]
    public void Expand_to_line_reads_the_enclosing_line_without_the_newline()
    {
        var s = new StringTextSurface(Doc, caretOffset: 14); // inside "second line"
        var r = s.GetCaret()!;
        r.ExpandToUnit(TextUnit.Line);
        r.GetText().Should().Be("second line");
    }

    [Fact]
    public void Expand_to_line_handles_crlf()
    {
        var s = new StringTextSurface("alpha\r\nbeta", caretOffset: 2);
        var r = s.GetCaret()!;
        r.ExpandToUnit(TextUnit.Line);
        r.GetText().Should().Be("alpha");
    }

    [Fact]
    public void Expand_to_word_keeps_apostrophes_and_hyphens_intact()
    {
        // The current word-echo splits on char.IsPunctuation and says "don"
        // then "t". A word is a run of non-whitespace.
        var s = new StringTextSurface("I don't like well-known bugs", caretOffset: 3);
        var r = s.GetCaret()!;
        r.ExpandToUnit(TextUnit.Word);
        r.GetText().Should().Be("don't");

        s.CaretOffset = 15;
        var r2 = s.GetCaret()!;
        r2.ExpandToUnit(TextUnit.Word);
        r2.GetText().Should().Be("well-known");
    }

    [Fact]
    public void Expand_to_character_is_grapheme_aware()
    {
        // A non-BMP emoji is two UTF-16 units but one character to a reader.
        var s = new StringTextSurface("a\U0001F600b", caretOffset: 1);
        var r = s.GetCaret()!;
        r.ExpandToUnit(TextUnit.Character);
        r.GetText().Should().Be("\U0001F600");
    }

    [Fact]
    public void Move_by_character_steps_over_a_surrogate_pair_as_one()
    {
        var s = new StringTextSurface("a\U0001F600b", caretOffset: 0);
        var r = s.GetCaret()!;
        r.Move(TextUnit.Character, 1).Should().Be(1);
        r.GetText().Should().Be("\U0001F600");
        r.Move(TextUnit.Character, 1).Should().Be(1);
        r.GetText().Should().Be("b");
    }

    [Fact]
    public void Move_backwards_over_a_surrogate_pair_lands_on_the_pair()
    {
        // "a" is [0], the emoji occupies [1..2], "b" is [3]. Starting on "b"
        // and stepping back must land on the whole emoji, not its trailing
        // surrogate half.
        var s = new StringTextSurface("a\U0001F600b", caretOffset: 3);
        var r = s.GetCaret()!;
        r.Move(TextUnit.Character, -1).Should().Be(-1);
        r.GetText().Should().Be("\U0001F600");

        r.Move(TextUnit.Character, -1).Should().Be(-1);
        r.GetText().Should().Be("a");
    }

    [Fact]
    public void Move_by_line_walks_the_document_and_stops_at_the_end()
    {
        var s = new StringTextSurface(Doc);
        var r = s.GetCaret()!;
        r.ExpandToUnit(TextUnit.Line);
        r.GetText().Should().Be("first line");

        r.Move(TextUnit.Line, 1).Should().Be(1);
        r.GetText().Should().Be("second line");

        r.Move(TextUnit.Line, 1).Should().Be(1);
        r.GetText().Should().Be("third line");

        // No fourth line: reports zero movement but still reads where it is.
        r.Move(TextUnit.Line, 1).Should().Be(0);
        r.GetText().Should().Be("third line");
    }

    [Fact]
    public void Move_by_word_walks_forward_and_back()
    {
        var s = new StringTextSurface("alpha beta gamma");
        var r = s.GetCaret()!;
        r.Move(TextUnit.Word, 1);
        r.GetText().Should().Be("beta");
        r.Move(TextUnit.Word, 1);
        r.GetText().Should().Be("gamma");
        r.Move(TextUnit.Word, -1);
        r.GetText().Should().Be("beta");
        r.Move(TextUnit.Word, -1);
        r.GetText().Should().Be("alpha");
        r.Move(TextUnit.Word, -1).Should().Be(0);
    }

    [Fact]
    public void Move_by_multiple_units_reports_how_far_it_actually_got()
    {
        var s = new StringTextSurface(Doc);
        var r = s.GetCaret()!;
        r.Move(TextUnit.Line, 10).Should().Be(2); // only two lines below the first
        r.GetText().Should().Be("third line");
    }

    [Fact]
    public void Compare_endpoints_orders_positions()
    {
        var s = new StringTextSurface(Doc);
        var a = s.RangeFromOffsets(3, 3);
        var b = s.RangeFromOffsets(8, 8);
        a.CompareEndpoints(RangeEndpoint.Start, b, RangeEndpoint.Start).Should().BeNegative();
        b.CompareEndpoints(RangeEndpoint.Start, a, RangeEndpoint.Start).Should().BePositive();
        a.CompareEndpoints(RangeEndpoint.Start, a, RangeEndpoint.Start).Should().Be(0);
    }

    [Fact]
    public void Set_endpoint_builds_a_span_between_two_positions()
    {
        var s = new StringTextSurface(Doc);
        var span = s.RangeFromOffsets(0, 0);
        var to = s.RangeFromOffsets(5, 5);
        span.SetEndpoint(RangeEndpoint.End, to, RangeEndpoint.Start);
        span.GetText().Should().Be("first");
    }

    [Fact]
    public void Selection_reports_the_selected_text()
    {
        var s = new StringTextSurface(Doc);
        s.Select(anchor: 0, active: 5);
        var sel = s.GetSelection()!;
        sel.IsCollapsed.Should().BeFalse();
        sel.GetText().Should().Be("first");
    }

    [Fact]
    public void Selection_works_backwards_too()
    {
        var s = new StringTextSurface(Doc);
        s.Select(anchor: 5, active: 0);
        s.GetSelection()!.GetText().Should().Be("first");
    }

    [Fact]
    public void Clone_is_independent()
    {
        var s = new StringTextSurface(Doc);
        var a = s.GetCaret()!;
        var b = a.Clone();
        b.Move(TextUnit.Line, 1);
        a.ExpandToUnit(TextUnit.Line);
        a.GetText().Should().Be("first line");
        b.GetText().Should().Be("second line");
    }

    [Fact]
    public void Get_text_honours_the_cap()
    {
        var s = new StringTextSurface(Doc);
        s.GetDocumentRange().GetText(5).Should().Be("first");
    }

    [Fact]
    public void Unsupported_units_degrade_rather_than_throw()
    {
        var s = new StringTextSurface(Doc, caretOffset: 14);
        s.SupportsUnit(TextUnit.Sentence).Should().BeFalse();
        var r = s.GetCaret()!;
        r.ExpandToUnit(TextUnit.Sentence); // degrades to Line
        r.GetText().Should().Be("second line");
    }

    [Fact]
    public void Empty_document_is_safe_to_navigate()
    {
        var s = new StringTextSurface(string.Empty);
        var r = s.GetCaret()!;
        r.Move(TextUnit.Character, 1).Should().Be(0);
        r.Move(TextUnit.Line, -1).Should().Be(0);
        r.ExpandToUnit(TextUnit.Line);
        r.GetText().Should().BeEmpty();
    }

    [Fact]
    public void Caret_past_the_last_character_reads_an_empty_character()
    {
        // The "end of line" case. The range is legitimately empty; turning
        // that into a spoken token is the speech layer's decision.
        var s = new StringTextSurface("abc", caretOffset: 3);
        var r = s.GetCaret()!;
        r.ExpandToUnit(TextUnit.Character);
        r.GetText().Should().BeEmpty();
    }

    [Fact]
    public void Blank_line_expands_to_empty()
    {
        var s = new StringTextSurface("a\n\nb", caretOffset: 2);
        var r = s.GetCaret()!;
        r.ExpandToUnit(TextUnit.Line);
        r.GetText().Should().BeEmpty();
    }
}
