namespace OpenReader.Input.Echo;

/// <summary>Per-feature toggles for keyboard echo.</summary>
/// <remarks>
/// <para>
/// <see cref="SpeakWords"/> is on by default so typing produces audible
/// feedback (Notepad, search boxes, etc.) without echoing every keystroke.
/// The other three default to off — modifier and navigation-key echo are
/// noisy ("control", "left", "right" on every press) and per-character
/// echo doubles every word with a stream of letters.
/// </para>
/// <para>
/// All four are surfaced in Settings → Keyboard so users can flip any of
/// them. Defaults reflect "what feels natural to most users on first run."
/// </para>
/// </remarks>
public sealed record KeyEchoSettings
{
    public bool SpeakModifiers { get; init; }
    public bool SpeakNavigationKeys { get; init; }
    public bool SpeakCharacters { get; init; }
    public bool SpeakWords { get; init; } = true;

    /// <summary>
    /// Speak the character that Backspace or Delete removes. <b>On by
    /// default, and independent of <see cref="SpeakCharacters"/>.</b>
    /// </summary>
    /// <remarks>
    /// Deletion is destructive and unverifiable by any other means. A sighted
    /// user glances at the line to confirm what vanished; without this the
    /// only way to find out is to navigate back over the text and re-read it.
    /// So it is not part of character echo: a user who finds per-character
    /// echo too chatty while typing still needs to know what they just
    /// destroyed.
    /// </remarks>
    public bool SpeakDeletedCharacters { get; init; } = true;

    public static KeyEchoSettings Defaults => new();
}
