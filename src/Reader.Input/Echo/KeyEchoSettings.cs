namespace Aura.Input.Echo;

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
    /// <summary>
    /// Speak named non-printable keys: Control, Alt, Windows, Shift, CapsLock,
    /// Tab, Escape, Enter, Backspace, Delete, Insert, arrows, Home/End,
    /// Page Up/Down, and the function keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One toggle rather than the previous two, because the split between
    /// "modifiers" and "navigation keys" did not match how anyone thinks about
    /// it — the question a user is answering is "do I want to hear key names?",
    /// and the answer is the same for Shift and for F7.
    /// </para>
    /// <para>
    /// <b>When this is off, no key name is ever spoken.</b> Not "backspace",
    /// not "tab", not under any fallback path. That is an invariant, covered by
    /// <c>CommandKeyEchoTests</c>, not merely the current behaviour.
    /// </para>
    /// <para>
    /// Distinct from deletion feedback, which announces the <em>character
    /// removed</em> by Backspace or Delete. That is content rather than a key
    /// name, it is not a setting at all, and it speaks when this is off — see
    /// <c>DeletionEchoTests</c>.
    /// </para>
    /// </remarks>
    public bool SpeakCommandKeys { get; init; }
    public bool SpeakCharacters { get; init; }
    public bool SpeakWords { get; init; } = true;

    /// <summary>
    /// Whether character and word echo also apply while in
    /// <see cref="Abstractions.Navigation.ReaderMode.Read"/>. Defaults to
    /// <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In Read mode a single letter is a <em>command</em>, not typed text —
    /// <c>h</c> jumps to the next heading. Echoing it announces "h" before the
    /// heading, which is noise for most users most of the time.
    /// </para>
    /// <para>
    /// It is a preference rather than a rule because some users navigate
    /// largely by quick-key and want the confirmation that the key registered,
    /// particularly on an unfamiliar keyboard. Off by default; on for those who
    /// want it.
    /// </para>
    /// <para>
    /// Only gates <see cref="SpeakCharacters"/> and <see cref="SpeakWords"/>.
    /// Deletion feedback is unaffected — deleting is destructive in either mode.
    /// </para>
    /// </remarks>
    public bool ApplyEchoInReadMode { get; init; }

    public static KeyEchoSettings Defaults => new();
}
