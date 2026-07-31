using FluentAssertions;
using OpenReader.Speech.Punctuation;
using Xunit;

namespace OpenReader.Speech.Tests;

public class PunctuationFilterTests
{
    [Fact]
    public void None_strips_all_punctuation()
    {
        var result = PunctuationFilter.Apply("hello, world!", PunctuationLevel.None);
        result.Should().Be("hello world");
    }

    [Fact]
    public void Some_keeps_sentence_terminators()
    {
        var result = PunctuationFilter.Apply("Hello, world! It works.", PunctuationLevel.Some);
        result.Should().Contain(",").And.Contain("!").And.Contain(".");
    }

    [Fact]
    public void Most_spells_brackets_but_keeps_terminators_silent()
    {
        var result = PunctuationFilter.Apply("foo (bar)", PunctuationLevel.Most);
        result.Should().Contain("left paren").And.Contain("right paren");
    }

    [Fact]
    public void All_spells_every_punctuation()
    {
        var result = PunctuationFilter.Apply("a,b", PunctuationLevel.All);
        result.Should().Contain("comma");
    }

    [Fact]
    public void Pure_letters_pass_through_untouched()
    {
        PunctuationFilter.Apply("hello world", PunctuationLevel.All).Should().Be("hello world");
        PunctuationFilter.Apply("hello world", PunctuationLevel.None).Should().Be("hello world");
    }

    [Fact]
    public void Empty_input_returns_empty()
    {
        PunctuationFilter.Apply(string.Empty, PunctuationLevel.All).Should().BeEmpty();
    }
}
