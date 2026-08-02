using FluentAssertions;
using Aura.Abstractions.Speech;
using Aura.Speech.Punctuation;
using Aura.Speech.Queue;
using Aura.Speech.Rules;
using Aura.TestKit;
using Xunit;

namespace Aura.Speech.Tests;

/// <summary>
/// Character-by-character navigation must name punctuation it lands on
/// ("," → "comma") regardless of the punctuation level — otherwise arrowing
/// across a line silently skips its symbols, which is what the user heard in
/// Notepad. Letters and digits are spoken verbatim.
/// </summary>
public class CharacterNavigationTests
{
    [Theory]
    [InlineData(",", "comma")]
    [InlineData(".", "dot")]
    [InlineData("(", "left paren")]
    [InlineData("/", "slash")]
    [InlineData(" ", "space")]
    public void Read_character_on_punctuation_speaks_its_name(string ch, string expected)
    {
        var nodes = new SyntheticTreeBuilder().Window("Test", w => w.Edit("Body", value: "x")).Build();
        using var provider = new SyntheticAccessibilityProvider(nodes);
        using var queue = new SpeechQueue();
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        using var pipeline = new SpeechPipeline(provider, engine, queue);

        // Default punctuation level is Some, which would otherwise strip "(" or
        // read "," as a silent pause.
        pipeline.Submit(new SpeechRequest(SpeechReason.ReadCharacter, Node: null, RawText: ch, AppExecutableName: null));

        var u = queue.WaitForNext(TimeSpan.FromSeconds(1));
        u.Should().NotBeNull();
        u!.Text.Should().Be(expected);
    }

    [Fact]
    public void Read_character_on_letter_is_spoken_verbatim()
    {
        var nodes = new SyntheticTreeBuilder().Window("Test", w => w.Edit("Body", value: "x")).Build();
        using var provider = new SyntheticAccessibilityProvider(nodes);
        using var queue = new SpeechQueue();
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        using var pipeline = new SpeechPipeline(provider, engine, queue);

        pipeline.Submit(new SpeechRequest(SpeechReason.ReadCharacter, Node: null, RawText: "a", AppExecutableName: null));

        var u = queue.WaitForNext(TimeSpan.FromSeconds(1));
        u.Should().NotBeNull();
        u!.Text.Should().Be("a");
    }

    [Theory]
    [InlineData(',', "comma")]
    [InlineData(' ', "space")]
    [InlineData('\t', "tab")]
    [InlineData('@', "at")]
    public void SpokenName_names_symbols_and_whitespace(char ch, string expected)
        => PunctuationFilter.SpokenName(ch).Should().Be(expected);

    [Theory]
    [InlineData('a')]
    [InlineData('Z')]
    [InlineData('7')]
    public void SpokenName_returns_null_for_letters_and_digits(char ch)
        => PunctuationFilter.SpokenName(ch).Should().BeNull();
}
