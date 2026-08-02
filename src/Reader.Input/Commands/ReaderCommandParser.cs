namespace Aura.Input.Commands;

/// <summary>
/// String ↔ <see cref="ReaderCommand"/> conversion for config files.
/// Matches enum names case-insensitively; returns false on unknown values
/// rather than throwing so an upgrade that drops a command silently no-ops
/// the binding instead of crashing the host.
/// </summary>
public static class ReaderCommandParser
{
    public static bool TryParse(string text, out ReaderCommand command)
        => Enum.TryParse(text, ignoreCase: true, out command) && command != ReaderCommand.None;

    public static string Format(ReaderCommand command) => command.ToString();
}
