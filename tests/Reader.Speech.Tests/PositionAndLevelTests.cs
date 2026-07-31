using FluentAssertions;
using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Speech;
using OpenReader.Speech.Rules;
using Xunit;

namespace OpenReader.Speech.Tests;

/// <summary>
/// Locks down the {position} / {setSize} / {level} / {posInSet} template
/// tokens and the read-only / caret-moved / combo-value-changed rules. These
/// fire in the user's day-to-day path (Explorer icons, Settings tree, Notepad
/// arrow-around-text), so silent regressions are particularly painful.
/// </summary>
public class PositionAndLevelTests
{
    private static AccessibleNode Node(
        AccessibleRole role,
        string? name = null,
        string? value = null,
        AccessibleStates states = AccessibleStates.None,
        IReadOnlyDictionary<string, object?>? extras = null)
        => new(new NodeId("n1"), role, name, value, description: null, states, parentId: null,
            childrenFactory: null, extras: extras);

    private static Dictionary<string, object?> Set(int position, int size, int? level = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["uia.PositionInSet"] = position,
            ["uia.SizeOfSet"] = size,
        };
        if (level.HasValue)
        {
            d["uia.Level"] = level.Value;
        }
        return d;
    }

    [Fact]
    public void List_item_announces_position_in_set()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var node = Node(AccessibleRole.ListItem, "report.txt", extras: Set(4, 10));
        var request = new SpeechRequest(SpeechReason.FocusChanged, node, RawText: null, AppExecutableName: null);

        var u = engine.Compose(request);

        u.Should().NotBeNull();
        u!.Text.Should().Contain("report.txt");
        u.Text.Should().Contain("list item");
        u.Text.Should().Contain("4 of 10");
    }

    [Fact]
    public void List_item_without_set_metadata_omits_position()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var node = Node(AccessibleRole.ListItem, "lonely");
        var request = new SpeechRequest(SpeechReason.FocusChanged, node, RawText: null, AppExecutableName: null);

        var u = engine.Compose(request);

        u.Should().NotBeNull();
        u!.Text.Should().Be("lonely, list item");
    }

    [Fact]
    public void Tree_item_announces_level_and_position()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var node = Node(AccessibleRole.TreeItem, "Documents", extras: Set(3, 8, level: 2));
        var request = new SpeechRequest(SpeechReason.FocusChanged, node, RawText: null, AppExecutableName: null);

        var u = engine.Compose(request);

        u.Should().NotBeNull();
        u!.Text.Should().Contain("Documents");
        u.Text.Should().Contain("level 2");
        u.Text.Should().Contain("3 of 8");
    }

    [Fact]
    public void Read_only_edit_emits_distinct_announcement()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var node = Node(AccessibleRole.Edit, "Description", value: "hello",
            states: AccessibleStates.ReadOnly);
        var request = new SpeechRequest(SpeechReason.FocusChanged, node, RawText: null, AppExecutableName: null);

        var u = engine.Compose(request);

        u.Should().NotBeNull();
        u!.Text.Should().Contain("read only edit");
    }

    [Fact]
    public void Caret_moved_announces_only_the_line()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var node = Node(AccessibleRole.Edit, "Doc");
        var request = new SpeechRequest(SpeechReason.CaretMoved, node, RawText: "second line", AppExecutableName: null);

        var u = engine.Compose(request);

        u.Should().NotBeNull();
        u!.Text.Should().Be("second line");
        u.CancelGroup.Should().Be("caret",
            "rapid arrow presses must preempt one another to feel snappy");
    }

    [Fact]
    public void Combo_value_change_speaks_just_the_new_value()
    {
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var node = Node(AccessibleRole.ComboBox, "Voice", value: "David");
        var request = new SpeechRequest(SpeechReason.ValueChanged, node, RawText: null, AppExecutableName: null);

        var u = engine.Compose(request);

        u.Should().NotBeNull();
        u!.Text.Should().Be("David");
    }

    [Fact]
    public void Edit_focus_announces_caret_line_not_whole_value()
    {
        // The provider captures the caret line into RawText on focus; {value}
        // holds the whole multi-line buffer. Focusing must read the LINE, not
        // dump the buffer (the "Notepad reads the whole document" bug).
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var node = Node(AccessibleRole.Edit, "Body", value: "line one\nline two\nline three");
        var request = new SpeechRequest(SpeechReason.FocusChanged, node, RawText: "line two", AppExecutableName: null);

        var u = engine.Compose(request);

        u.Should().NotBeNull();
        u!.Text.Should().Be("Body, edit, line two");
        u.Text.Should().NotContain("line one");
        u.Text.Should().NotContain("line three");
    }

    [Fact]
    public void Single_line_edit_still_reads_its_value_on_focus()
    {
        // A single-line edit's caret line IS its full value, so search boxes /
        // the Run box keep reading their content — only multi-line edits are
        // trimmed to the current line.
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var node = Node(AccessibleRole.Edit, "Open");
        var request = new SpeechRequest(SpeechReason.FocusChanged, node, RawText: "notepad", AppExecutableName: null);

        var u = engine.Compose(request);

        u.Should().NotBeNull();
        u!.Text.Should().Be("Open, edit, notepad");
    }

    [Fact]
    public void Password_edit_announces_role_only_never_content()
    {
        // Windows password boxes arrive as role Edit + Protected (no distinct
        // control type). Even if a caret line leaked into RawText, the
        // Protected-scoped rule must win and speak only the role.
        var engine = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        var node = Node(AccessibleRole.Edit, "Password", value: "hunter2",
            states: AccessibleStates.Protected);
        var request = new SpeechRequest(SpeechReason.FocusChanged, node, RawText: "hunter2", AppExecutableName: null);

        var u = engine.Compose(request);

        u.Should().NotBeNull();
        u!.Text.Should().Be("Password, password edit");
        u.Text.Should().NotContain("hunter2");
    }
}
