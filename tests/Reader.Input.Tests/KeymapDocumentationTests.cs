using System.Reflection;
using FluentAssertions;
using Aura.Input.Commands;
using Aura.Input.Gestures;
using Xunit;

namespace Aura.Input.Tests;

/// <summary>
/// Keeps <c>docs/KEYMAP.md</c> honest.
/// </summary>
/// <remarks>
/// <para>
/// The keymap had drifted badly before this existed: the documented numpad
/// grid did not match the code at all, and six commands were bound but
/// undocumented. Nobody noticed, because nothing could.
/// </para>
/// <para>
/// Documentation that describes an interface a user drives with their hands,
/// while unable to see the screen, is not a nicety — a wrong keymap is a
/// broken feature. So it gets a test, like anything else that can be wrong.
/// </para>
/// </remarks>
public class KeymapDocumentationTests
{
    /// <summary>
    /// Commands with no default chord, and why. Anything else unbound is a
    /// command someone forgot to wire up.
    /// </summary>
    private static readonly Dictionary<ReaderCommand, string> IntentionallyUnbound = new()
    {
        [ReaderCommand.None] = "sentinel, not a command",
        [ReaderCommand.ReportDate] = "reached by double-tapping Reader+F12",
        [ReaderCommand.ToggleEnabled] = "reached by double-tapping CapsLock, or the tray menu",
        [ReaderCommand.OpenSettings] = "reached from the Aura menu (Reader+A), like NVDA's",
    };

    private static string KeymapPath()
    {
        // Walk up from the test binary to the repository root.
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AURA.slnx")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("the repository root should be findable from the test binary");
        return Path.Combine(dir!.FullName, "docs", "KEYMAP.md");
    }

    private static GestureMap MapFor(KeyboardLayout layout)
    {
        var map = new GestureMap();
        GestureBindings.ApplyDefaults(map, layout);
        return map;
    }

    [Fact]
    public void Every_command_is_either_bound_or_explicitly_exempt()
    {
        var bound = new HashSet<ReaderCommand>();
        foreach (var layout in new[] { KeyboardLayout.Desktop, KeyboardLayout.Laptop })
        {
            foreach (var (_, _, command) in MapFor(layout).SnapshotAllLayers())
            {
                bound.Add(command);
            }
        }

        var missing = Enum.GetValues<ReaderCommand>()
            .Where(c => !bound.Contains(c) && !IntentionallyUnbound.ContainsKey(c))
            .ToList();

        missing.Should().BeEmpty(
            "every command needs a default chord or an entry in IntentionallyUnbound explaining why not");
    }

    [Fact]
    public void Every_bound_command_appears_in_the_keymap_documentation()
    {
        var doc = File.ReadAllText(KeymapPath());

        var undocumented = new List<string>();
        foreach (var layout in new[] { KeyboardLayout.Desktop, KeyboardLayout.Laptop })
        {
            foreach (var (_, _, command) in MapFor(layout).SnapshotAllLayers())
            {
                // Match the exact command name in backticks. That is what a
                // user has to type into Input.KeyBindings, so the doc has to
                // carry it verbatim — matching prose instead would let the
                // reference table drift while the test stayed green.
                if (!doc.Contains($"`{command}`", StringComparison.Ordinal))
                {
                    undocumented.Add(command.ToString());
                }
            }
        }

        undocumented.Distinct().Should().BeEmpty("docs/KEYMAP.md must describe every default binding");
    }

    [Fact]
    public void The_command_reference_lists_every_command_including_unbound_ones()
    {
        // Someone editing config.json needs the exact name, and the two
        // commands with no default chord are exactly the ones they are most
        // likely to want to bind.
        var doc = File.ReadAllText(KeymapPath());
        foreach (var command in Enum.GetValues<ReaderCommand>())
        {
            if (command == ReaderCommand.None)
            {
                continue;
            }
            doc.Should().Contain($"`{command}`", "the command reference must list {0}", command);
        }
    }

    [Fact]
    public void The_exemption_list_does_not_rot()
    {
        // An exemption for a command that has since gained a binding is stale
        // and should be removed, or it will hide a real gap later.
        foreach (var layout in new[] { KeyboardLayout.Desktop, KeyboardLayout.Laptop })
        {
            foreach (var (_, _, command) in MapFor(layout).SnapshotAllLayers())
            {
                IntentionallyUnbound.ContainsKey(command).Should().BeFalse(
                    "{0} is bound, so its IntentionallyUnbound entry is stale", command);
            }
        }
    }

    [Fact]
    public void Both_layouts_bind_the_core_reading_commands()
    {
        // Switching layout must not silently lose the ability to read.
        ReaderCommand[] essential =
        [
            ReaderCommand.ReadCharacter, ReaderCommand.ReadNextCharacter, ReaderCommand.ReadPreviousCharacter,
            ReaderCommand.ReadWord, ReaderCommand.ReadNextWord, ReaderCommand.ReadPreviousWord,
            ReaderCommand.ReadLine, ReaderCommand.ReadNextLine, ReaderCommand.ReadPreviousLine,
            ReaderCommand.StopSpeech, ReaderCommand.SayAllFromCursor,
            ReaderCommand.ReviewMoveToTop, ReaderCommand.ReviewMoveToBottom,
        ];

        foreach (var layout in new[] { KeyboardLayout.Desktop, KeyboardLayout.Laptop })
        {
            var bound = MapFor(layout).SnapshotAllLayers().Select(b => b.Command).ToHashSet();
            foreach (var command in essential)
            {
                bound.Should().Contain(command, "{0} must be reachable in the {1} layout", command, layout);
            }
        }
    }

    [Fact]
    public void The_diagnostics_command_is_documented_for_bug_reports()
    {
        // Testers are told to press this. If it stops being documented or
        // bound, reports lose the only facts they carry.
        var doc = File.ReadAllText(KeymapPath());
        doc.Should().Contain("Ctrl+Reader+D");

        MapFor(KeyboardLayout.Desktop).SnapshotAllLayers()
            .Select(b => b.Command)
            .Should().Contain(ReaderCommand.ReportDiagnostics);
    }
}
