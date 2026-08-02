using FluentAssertions;
using OpenReader.Abstractions.Text;
using Xunit;

namespace OpenReader.Input.Tests;

/// <summary>
/// The word rule is shared between typing echo and the text model, so these
/// assertions hold for both. Before <see cref="WordBoundary"/> existed the two
/// disagreed: review said "don't", echo said "don" then "t".
/// </summary>
public class KeyEchoWordSplittingTests
{
    [Theory]
    [InlineData('\'')]
    [InlineData('-')]
    [InlineData('_')]
    public void Characters_that_live_inside_words_do_not_terminate_them(char c)
    {
        WordBoundary.IsTerminator(c).Should().BeFalse();
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('\t')]
    [InlineData('\n')]
    [InlineData('.')]
    [InlineData(',')]
    [InlineData('!')]
    [InlineData('?')]
    [InlineData(';')]
    [InlineData(')')]
    [InlineData('/')]
    public void Characters_that_end_a_word_do_terminate_it(char c)
    {
        WordBoundary.IsTerminator(c).Should().BeTrue();
    }

    [Fact]
    public void Only_whitespace_separates_words_for_navigation()
    {
        // Navigation must not stop inside "don't", even though a full stop
        // would end the word while typing. Terminator and separator are
        // deliberately different sets.
        WordBoundary.IsSeparator(' ').Should().BeTrue();
        WordBoundary.IsSeparator('.').Should().BeFalse();
        WordBoundary.IsSeparator('\'').Should().BeFalse();
    }

    [Fact]
    public void The_text_model_agrees_with_the_echo_rule()
    {
        // The regression this guards: two components deriving "a word"
        // independently and drifting apart.
        var surface = new StringTextSurface("I don't like well-known bugs", caretOffset: 3);
        var range = surface.GetCaret()!;
        range.ExpandToUnit(TextUnit.Word);
        range.GetText().Should().Be("don't");
    }
}
