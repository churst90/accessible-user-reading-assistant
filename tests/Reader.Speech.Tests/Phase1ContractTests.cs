using FluentAssertions;
using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Speech;
using OpenReader.Speech.Queue;
using OpenReader.Speech.Rules;
using OpenReader.TestKit;
using Xunit;

namespace OpenReader.Speech.Tests;

/// <summary>
/// The canonical Phase-1 contract: focus a button, hear it announced.
/// When these pass against the synthetic provider, the speech-side of Phase 1
/// is structurally complete; the platform side (UIA, SAPI5) is verified by hand.
/// </summary>
public class Phase1ContractTests
{
    [Fact]
    public void Focus_on_button_produces_utterance_naming_role_and_label()
    {
        var nodes = new SyntheticTreeBuilder()
            .Window("Test", w => w.Button("OK"))
            .Build();

        using var provider = new SyntheticAccessibilityProvider(nodes);
        using var queue = new SpeechQueue();
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        using var pipeline = new SpeechPipeline(provider, engine, queue);
        pipeline.Start();

        provider.SimulateFocus("OK");

        var utterance = queue.WaitForNext(TimeSpan.FromSeconds(1));
        utterance.Should().NotBeNull();
        utterance!.Text.Should().Contain("OK").And.Contain("button");
    }

    [Fact]
    public void Read_character_for_edit_with_value_speaks_single_character()
    {
        var nodes = new SyntheticTreeBuilder()
            .Window("Test", w => w.Edit("Body", value: "hello"))
            .Build();

        using var provider = new SyntheticAccessibilityProvider(nodes);
        using var queue = new SpeechQueue();
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        using var pipeline = new SpeechPipeline(provider, engine, queue);
        pipeline.Start();

        // Phase 1 has no review cursor yet; drive the read-character pathway directly via Submit.
        var edit = nodes.First(n => n.Name == "Body");
        var submitted = pipeline.Submit(new SpeechRequest(SpeechReason.ReadCharacter, edit, RawText: "h", AppExecutableName: null));

        submitted.Should().BeTrue();
        var utterance = queue.WaitForNext(TimeSpan.FromSeconds(1));
        utterance.Should().NotBeNull();
        utterance!.Text.Should().Be("h");
    }

    [Fact]
    public void Suppression_rule_drops_matching_utterance()
    {
        // GIVEN built-in defaults plus a high-priority user rule that suppresses tooltips.
        var rules = new List<SpeechRule>(YamlRuleLoader.LoadDefaults())
        {
            new("user.suppress.tooltip", Priority: 100,
                new SpeechRuleScope(Role: AccessibleRole.ToolTip),
                new SpeechRuleAction.Suppress()),
        };
        var engine = new SpeechRuleEngine(rules);

        var tooltip = new AccessibleNode(
            id: new NodeId("t1"),
            role: AccessibleRole.ToolTip,
            name: "Save your work",
            value: null,
            description: null,
            states: AccessibleStates.None,
            parentId: null);

        var u = engine.Compose(new SpeechRequest(SpeechReason.FocusChanged, tooltip, RawText: null, AppExecutableName: null));

        u.Should().BeNull();
    }
}
