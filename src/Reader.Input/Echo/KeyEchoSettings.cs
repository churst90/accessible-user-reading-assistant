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

    public static KeyEchoSettings Defaults => new();
}
