using Aura.Diagnostics;
using Aura.Input.Commands;

namespace Aura.Input.Gestures;

/// <summary>
/// Applies a dictionary of <c>"chord-string" → "command-name"</c> entries to
/// a <see cref="GestureMap"/> already populated with layout defaults.
/// </summary>
/// <remarks>
/// Use after <see cref="GestureBindings.Reset"/>. Unparseable entries are
/// logged and skipped — never throw — so a typo in user config doesn't
/// crash the host.
/// </remarks>
public static class KeyBindingApplier
{
    public static int ApplyOverrides(GestureMap map, IReadOnlyDictionary<string, string>? overrides)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (overrides is null || overrides.Count == 0)
        {
            return 0;
        }

        var log = LoggerFactory.ForComponent("Input.Bindings");
        var applied = 0;
        foreach (var (chordText, commandText) in overrides)
        {
            if (!KeyChordParser.TryParse(chordText, out var chord))
            {
                log.Warning("ignoring unparseable chord '{Chord}'", chordText);
                continue;
            }
            if (!ReaderCommandParser.TryParse(commandText, out var command))
            {
                log.Warning("ignoring unknown command '{Command}' for chord '{Chord}'", commandText, chordText);
                continue;
            }
            map.Bind(chord, command);
            applied++;
        }
        if (applied > 0)
        {
            log.Information("applied {Count} user keybinding override(s)", applied);
        }
        return applied;
    }
}
