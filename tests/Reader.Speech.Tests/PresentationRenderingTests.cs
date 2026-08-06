using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Output;
using Aura.Abstractions.Speech;
using Aura.Abstractions.Text;
using Aura.Speech.Rendering;
using Aura.Speech.Rules;
using FluentAssertions;
using Xunit;

namespace Aura.Speech.Tests;

/// <summary>
/// The output model: what a rule template decomposes into, and what that
/// renders to.
/// </summary>
public class PresentationRenderingTests
{
    private static AccessibleNode Node(
        AccessibleRole role, string? name = null, string? value = null,
        IDictionary<string, object?>? extras = null) =>
        new(new NodeId("n1"), role, name, value, null, AccessibleStates.None, null, () => [],
            extras is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(extras));

    private static Presentation Compose(string template, SpeechRequest request)
    {
        var engine = new SpeechRuleEngine([
            new SpeechRule("t", 100, new SpeechRuleScope(), new SpeechRuleAction.Emit(template)),
        ]);
        return engine.Compose(request)!;
    }

    private static SpeechRequest Focus(AccessibleNode? node, string? text = null) =>
        new(SpeechReason.FocusChanged, node, text, null);

    // ---- decomposition ----

    [Fact]
    public void A_template_splits_into_one_segment_per_comma()
    {
        var p = Compose("{name}, button", Focus(Node(AccessibleRole.Button, "OK")));

        // "button" is tagged Role rather than Literal because it IS this node's
        // role word. Templates say "{name}, button" instead of "{name}, {role}"
        // — the role word is often more specific than the enum, and nothing in
        // the string says which part of it is the role. Matching against the
        // formatted role recovers that, which is what lets verbosity drop role
        // words without rewriting forty templates first.
        TranscriptRenderer.RenderTyped(p).Should().Be("Name(OK) Role(button)");
    }

    [Fact]
    public void A_literal_that_is_not_the_role_word_stays_a_literal()
    {
        var p = Compose("{name}, not checked", Focus(Node(AccessibleRole.CheckBox, "Enable")));

        TranscriptRenderer.RenderTyped(p).Should().Be("Name(Enable) Literal(not checked)");
    }

    [Fact]
    public void Verbosity_drops_a_whole_kind_of_segment()
    {
        // The reason SegmentKind exists. "Stop telling me it is a list item" is
        // one filter over a list, not a change to every rule that mentions one.
        var p = Compose("{name}, list item, {posInSet}", Focus(Node(
            AccessibleRole.ListItem, "report.txt",
            extras: new Dictionary<string, object?>
            {
                [NodeExtras.PositionInSet] = 4,
                [NodeExtras.SizeOfSet] = 10,
            })));

        new SpeechRenderer().Render(p).PlainText().Should().Be("report.txt, list item, 4 of 10");

        var terse = new SpeechRenderer
        {
            Verbosity = new Verbosity { ReportRole = false, ReportPosition = false },
        };
        terse.Render(p).PlainText().Should().Be("report.txt");
    }

    [Fact]
    public void Verbosity_can_never_silence_the_thing_itself()
    {
        // There is no switch for Name or Content, and there must not be: a
        // reader that can be configured to stop saying what it is looking at
        // has no remaining purpose.
        var everythingOff = new Verbosity
        {
            ReportRole = false,
            ReportPosition = false,
            ReportState = false,
            ReportDescription = false,
            ReportHints = false,
        };

        everythingOff.Allows(SegmentKind.Name).Should().BeTrue();
        everythingOff.Allows(SegmentKind.Content).Should().BeTrue();
    }

    [Fact]
    public void A_label_stays_with_its_value_in_one_segment()
    {
        // "level {level}" must not become "level" and "2" — joining those with
        // the separator would say "level, 2".
        var p = Compose("level {level}", Focus(Node(AccessibleRole.TreeItem,
            extras: new Dictionary<string, object?> { [NodeExtras.Level] = 2 })));

        TranscriptRenderer.RenderTyped(p).Should().Be("Position(level 2)");
        p.Spoken().Should().Be("level 2");
    }

    [Fact]
    public void A_segment_whose_token_is_empty_is_dropped_whole()
    {
        // A label with no value ("level ,") is noise. Dropping the segment is
        // what replaces the old trailing-comma string tidying.
        var p = Compose("{name}, tree item, level {level}", Focus(Node(AccessibleRole.TreeItem, "Documents")));

        p.Spoken().Should().Be("Documents, tree item");
    }

    [Fact]
    public void A_missing_name_leaves_no_leading_separator()
    {
        var p = Compose("{name}, window", Focus(Node(AccessibleRole.Window)));

        p.Spoken().Should().Be("window");
    }

    [Fact]
    public void An_unknown_token_is_left_in_place_so_the_typo_is_audible()
    {
        var p = Compose("{nmae}, button", Focus(Node(AccessibleRole.Button, "OK")));

        p.Spoken().Should().Be("{nmae}, button");
    }

    [Fact]
    public void A_template_can_declare_a_segment_kind()
    {
        // The migration path for turning today's literal role and state words
        // into filterable kinds, without a contract change.
        var p = Compose("{name}, {state:checked}", Focus(Node(AccessibleRole.CheckBox, "Enable")));

        TranscriptRenderer.RenderTyped(p).Should().Be("Name(Enable) State(checked)");
    }

    // ---- rendering ----

    [Fact]
    public void A_lone_capital_gets_a_pitch_bump_rather_than_the_word_cap()
    {
        // Announcing "cap" costs a whole word per character, which is unusable
        // at reading speed.
        var renderer = new SpeechRenderer();
        var p = Compose("{text}", new SpeechRequest(SpeechReason.ReadCharacter, null, "A", null));

        var u = renderer.Render(p);

        u.Parts.Should().ContainItemsAssignableTo<OutputPart>();
        u.Parts.OfType<ProsodyPush>().Should().ContainSingle()
            .Which.Delta.PitchDelta.Should().BeGreaterThan(0);
        u.Parts.OfType<ProsodyPop>().Should().ContainSingle();
        u.PlainText().Should().Be("A");
    }

    [Fact]
    public void A_lowercase_letter_gets_no_pitch_bump()
    {
        var renderer = new SpeechRenderer();
        var p = Compose("{text}", new SpeechRequest(SpeechReason.ReadCharacter, null, "a", null));

        renderer.Render(p).Parts.OfType<ProsodyPush>().Should().BeEmpty();
    }

    [Fact]
    public void A_run_in_another_language_switches_voice_and_switches_back()
    {
        // The reason a flat string could not do this, and the reason
        // TextAttributes.Language existed with nothing able to carry it.
        var renderer = new SpeechRenderer();
        var p = new Presentation(
            Segments: [
                new PresentationSegment("bonjour", SegmentKind.Content,
                    Attributes: new Dictionary<string, object?> { [TextAttributes.Language] = "fr-FR" }),
                new PresentationSegment("link", SegmentKind.Role),
            ],
            Reason: SpeechReason.FocusChanged, Subject: "n1",
            Priority: SpeechPriority.Next, CancelGroup: null, Validity: null, RuleTrace: []);

        var parts = renderer.Render(p).Parts;

        parts.OfType<LanguagePart>().Select(l => l.BcpTag).Should().Equal("fr-FR", null);
    }

    [Fact]
    public void The_readers_own_words_are_never_in_the_documents_language()
    {
        // Otherwise "button" gets a French accent inside a French page.
        var renderer = new SpeechRenderer();
        var p = new Presentation(
            Segments: [
                new PresentationSegment("Envoyer", SegmentKind.Name,
                    Attributes: new Dictionary<string, object?> { [TextAttributes.Language] = "fr-FR" }),
                new PresentationSegment("button", SegmentKind.Role,
                    Attributes: new Dictionary<string, object?> { [TextAttributes.Language] = "fr-FR" }),
            ],
            Reason: SpeechReason.FocusChanged, Subject: "n1",
            Priority: SpeechPriority.Next, CancelGroup: null, Validity: null, RuleTrace: []);

        var parts = renderer.Render(p).Parts;
        var languages = parts.OfType<LanguagePart>().Select(l => l.BcpTag).ToList();

        languages.Should().Equal("fr-FR", null);
    }

    // ---- blankness ----

    [Fact]
    public void A_presentation_is_blank_only_when_nothing_in_it_is_audible()
    {
        // An empty line inside a list item is not blank — the list item is
        // audible. This is the distinction that comparing one announcement's
        // text against the previous one could never make, and it is why
        // arrowing through blank lines went silent.
        var empty = new Presentation(
            [new PresentationSegment("   ", SegmentKind.Content)],
            SpeechReason.CaretMoved, "doc", SpeechPriority.Next, null, null, []);
        var inListItem = new Presentation(
            [new PresentationSegment("", SegmentKind.Content),
             new PresentationSegment("list item", SegmentKind.Literal)],
            SpeechReason.CaretMoved, "doc", SpeechPriority.Next, null, null, []);

        empty.IsBlank.Should().BeTrue();
        inListItem.IsBlank.Should().BeFalse();
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("\r\n", true)]
    [InlineData(" ", true)]   // non-breaking space: looks empty, is not
    [InlineData("​", true)]   // zero-width space
    [InlineData("a", false)]
    [InlineData(" . ", false)]
    public void Blank_covers_the_characters_that_look_empty_and_are_not(string text, bool expected)
        => Blank.Is(text).Should().Be(expected);
}
