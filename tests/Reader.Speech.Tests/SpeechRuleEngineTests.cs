using FluentAssertions;
using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Speech;
using Aura.Speech.Rules;
using Aura.TestKit;
using Xunit;

namespace Aura.Speech.Tests;

public class SpeechRuleEngineTests
{
    private static AccessibleNode Node(AccessibleRole role, string? name = null, string? value = null, AccessibleStates states = AccessibleStates.None)
        => new(new NodeId("n1"), role, name, value, description: null, states, parentId: null);

    [Fact]
    public void Defaults_load_from_embedded_resource()
    {
        var rules = YamlRuleLoader.LoadDefaults();
        rules.Should().NotBeEmpty();
        rules.Should().Contain(r => r.Id == "core.role.button");
    }

    [Fact]
    public void Button_focus_emits_name_and_role()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var request = new SpeechRequest(SpeechReason.FocusChanged, Node(AccessibleRole.Button, "OK"), RawText: null, AppExecutableName: null);

        var u = engine.Compose(request);

        u.Should().NotBeNull();
        u!.Text.Should().Contain("OK").And.Contain("button");
        u.CancelGroup.Should().Be("focus");
        u.Priority.Should().Be(SpeechPriority.Next);
    }

    [Fact]
    public void Menu_item_focus_speaks_name_and_role()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var request = new SpeechRequest(SpeechReason.FocusChanged, Node(AccessibleRole.MenuItem, "Open"), RawText: null, AppExecutableName: null);

        var u = engine.Compose(request);

        u.Should().NotBeNull();
        u!.Text.Should().Be("Open, menu item");
    }

    [Fact]
    public void Checked_state_promotes_higher_priority_rule()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var request = new SpeechRequest(
            SpeechReason.FocusChanged,
            Node(AccessibleRole.CheckBox, "Bold", states: AccessibleStates.Checked),
            RawText: null, AppExecutableName: null);

        var u = engine.Compose(request);

        u!.Text.Should().Contain("checked").And.NotContain("not checked");
    }

    [Fact]
    public void Suppress_action_returns_null()
    {
        var rules = new[]
        {
            new SpeechRule("core.role.button", 10, new SpeechRuleScope(Role: AccessibleRole.Button), new SpeechRuleAction.Emit("{name}, button")),
            new SpeechRule("user.suppress.button", 100, new SpeechRuleScope(Role: AccessibleRole.Button), new SpeechRuleAction.Suppress()),
        };
        var engine = new SpeechRuleEngine(rules);

        var u = engine.Compose(new SpeechRequest(SpeechReason.FocusChanged, Node(AccessibleRole.Button, "Cancel"), null, null));

        u.Should().BeNull();
    }

    [Fact]
    public void Rewrite_modifies_emitted_text()
    {
        var rules = new[]
        {
            new SpeechRule("emit", 10, new SpeechRuleScope(Role: AccessibleRole.Button), new SpeechRuleAction.Emit("{name}, button")),
            new SpeechRule("rewrite", 5, new SpeechRuleScope(Role: AccessibleRole.Button), new SpeechRuleAction.Rewrite("button", "btn")),
        };
        var engine = new SpeechRuleEngine(rules);

        var u = engine.Compose(new SpeechRequest(SpeechReason.FocusChanged, Node(AccessibleRole.Button, "OK"), null, null));

        u!.Text.Should().Be("OK, btn");
    }

    [Fact]
    public void Disabled_state_overlay_appends_disabled()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var request = new SpeechRequest(
            SpeechReason.FocusChanged,
            Node(AccessibleRole.Button, "Save", states: AccessibleStates.Disabled),
            null, null);

        var u = engine.Compose(request);

        u!.Text.Should().EndWith(", disabled");
    }

    [Fact]
    public void No_matching_rule_returns_null()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var request = new SpeechRequest(SpeechReason.FocusChanged, Node(AccessibleRole.Custom, "X"), null, null);

        var u = engine.Compose(request);

        u.Should().BeNull();
    }

    [Fact]
    public void Read_character_speaks_raw_text()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var request = new SpeechRequest(SpeechReason.ReadCharacter, Node: null, RawText: "h", AppExecutableName: null);

        var u = engine.Compose(request);

        u!.Text.Should().Be("h");
    }

    [Fact]
    public void Empty_node_renders_role_label_only()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var request = new SpeechRequest(SpeechReason.FocusChanged, Node(AccessibleRole.Button, name: null), null, null);

        var u = engine.Compose(request);

        u!.Text.Should().Be("button");
    }

    [Fact]
    public void RuleTrace_lists_applied_rules()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var request = new SpeechRequest(
            SpeechReason.FocusChanged,
            Node(AccessibleRole.Button, "Save", states: AccessibleStates.Disabled),
            null, null);

        var u = engine.Compose(request);

        u!.RuleTrace.Should().Contain("core.role.button").And.Contain("core.state.disabled");
    }

    [Fact]
    public void Synthetic_provider_focus_drives_pipeline()
    {
        var nodes = new SyntheticTreeBuilder().Window("Test", w => w.Button("OK")).Build();
        using var provider = new SyntheticAccessibilityProvider(nodes);
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        using var queue = new Queue.SpeechQueue();
        using var pipeline = new SpeechPipeline(provider, engine, queue);
        pipeline.Start();

        provider.SimulateFocus("OK");

        var u = queue.WaitForNext(TimeSpan.FromSeconds(1));
        u.Should().NotBeNull();
        u!.Text.Should().Contain("OK").And.Contain("button");
    }
}
