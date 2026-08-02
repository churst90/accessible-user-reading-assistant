namespace Aura.Config;

/// <summary>
/// Top-level user-facing configuration. Serialized as JSON; one instance is
/// produced per resolved layer and merged into a final composite at runtime.
/// </summary>
/// <remarks>
/// All sections and properties are nullable. A null at any layer means
/// "inherit from the layer below" (defaults at the bottom). The merge logic
/// in <c>ConfigStore</c> walks the property tree and replaces with the
/// highest layer that supplied a non-null value.
/// </remarks>
public sealed record ReaderConfig
{
    public int? Version { get; init; }
    public GeneralConfig? General { get; init; }
    public SpeechConfig? Speech { get; init; }
    public InputConfig? Input { get; init; }
    public KeyboardConfig? Keyboard { get; init; }
    public DiagnosticsConfig? Diagnostics { get; init; }

    public static ReaderConfig Defaults() => new()
    {
        Version = 1,
        General = GeneralConfig.Defaults(),
        Speech = SpeechConfig.Defaults(),
        Input = InputConfig.Defaults(),
        Keyboard = KeyboardConfig.Defaults(),
        Diagnostics = DiagnosticsConfig.Defaults(),
    };
}

/// <summary>Logging and troubleshooting preferences.</summary>
public sealed record DiagnosticsConfig
{
    /// <summary>
    /// Keep spoken text out of the log file. <b>Defaults to true</b> and should
    /// stay that way for anyone who is not actively debugging their own
    /// machine: a screen reader speaks passwords, banking pages and private
    /// messages, and the log survives reboots and gets attached to bug reports.
    /// </summary>
    public bool? RedactContent { get; init; }

    /// <summary>Minimum log level — <c>verbose</c>, <c>debug</c>, <c>information</c>, <c>warning</c>, <c>error</c>.</summary>
    public string? MinimumLevel { get; init; }

    public static DiagnosticsConfig Defaults() => new()
    {
        RedactContent = true,
        MinimumLevel = "information",
    };
}

/// <summary>Top-level, app-wide preferences not specific to a subsystem.</summary>
public sealed record GeneralConfig
{
    /// <summary>Active profile name. <c>"default"</c> means the user-layer config is used directly.</summary>
    public string? Profile { get; init; }

    /// <summary>Auto-start with Windows. Surfaces as a checkbox; the host wires the registry entry.</summary>
    public bool? StartWithWindows { get; init; }

    public static GeneralConfig Defaults() => new()
    {
        Profile = "default",
        StartWithWindows = false,
    };
}

/// <summary>Choice of keyboard layout for review-cursor commands.</summary>
public enum KeyboardLayoutKind
{
    /// <summary>Insert is the Reader modifier; the numpad drives review.</summary>
    Desktop = 0,

    /// <summary>CapsLock is the Reader modifier; main arrow cluster drives review.</summary>
    Laptop = 1,
}

/// <summary>Keyboard preferences: layout, key echo, and modifier announcements.</summary>
public sealed record KeyboardConfig
{
    /// <summary>Active layout. <c>desktop</c> or <c>laptop</c>.</summary>
    public string? Layout { get; init; }

    /// <summary>
    /// Speak named non-printable keys — Control, Alt, Windows, Shift,
    /// CapsLock, Tab, Escape, Enter, Backspace, Delete, arrows, function keys.
    /// When false, no key name is spoken under any circumstance.
    /// </summary>
    public bool? SpeakCommandKeys { get; init; }

    /// <summary>Echo each typed character.</summary>
    public bool? SpeakCharacters { get; init; }

    /// <summary>Echo each completed word (after a word break: space, enter, punctuation).</summary>
    public bool? SpeakWords { get; init; }

    /// <summary>
    /// Speak the character Backspace or Delete removes. Defaults to
    /// <c>true</c> and is deliberately independent of
    /// <see cref="SpeakCharacters"/> — deletion is destructive, and knowing
    /// what vanished is not the same want as hearing every keystroke.
    /// </summary>
    public bool? SpeakDeletedCharacters { get; init; }

    public static KeyboardConfig Defaults() => new()
    {
        Layout = "desktop",
        // Word echo on so typing in Notepad / search boxes is audible without
        // a stream of "left right down" or "k a t" — the noisy ones default off.
        // Surfaced as four checkboxes in Settings → Keyboard.
        SpeakCommandKeys = false,
        SpeakCharacters = false,
        SpeakWords = true,
        SpeakDeletedCharacters = true,
    };
}

/// <summary>Speech-related preferences. Composes with the rule engine and queue.</summary>
public sealed record SpeechConfig
{
    /// <summary>Engine identifier (e.g. <c>sapi5</c>). Future engines (espeak-ng, azure) plug in here.</summary>
    public string? Engine { get; init; }

    /// <summary>Voice id within the chosen engine. Null = engine default.</summary>
    public string? VoiceId { get; init; }

    /// <summary>Default rate as percent of engine baseline (25..400, 100 = neutral).</summary>
    public float? RatePercent { get; init; }

    /// <summary>Default volume delta in percent points (-100..+100).</summary>
    public float? VolumeDelta { get; init; }

    /// <summary>Default pitch delta in semitones (-12..+12).</summary>
    public float? PitchDelta { get; init; }

    /// <summary>
    /// How the screen reader signals capital letters when reading individual
    /// characters. <c>"off"</c>, <c>"pitch"</c> (raise pitch on the letter),
    /// <c>"beep"</c> (audio cue — needs Phase 4b audio mixer), or <c>"both"</c>.
    /// </summary>
    public string? CapitalLetterAnnouncement { get; init; }

    public static SpeechConfig Defaults() => new()
    {
        Engine = "sapi5",
        VoiceId = null,
        RatePercent = 100f,
        VolumeDelta = 0f,
        PitchDelta = 0f,
        CapitalLetterAnnouncement = "pitch",
    };
}

/// <summary>Input-related preferences. Drives gesture-map overrides and modifier choice.</summary>
public sealed record InputConfig
{
    /// <summary>Which physical key acts as the Reader modifier — <c>insert</c>, <c>capslock</c>, or <c>both</c>.</summary>
    public string? ReaderModifier { get; init; }

    /// <summary>
    /// User-supplied keybinding overrides. Each entry rebinds one chord. The
    /// chord syntax is <c>"Reader+Down"</c>, <c>"Ctrl+Shift+P"</c>, etc.
    /// </summary>
    public Dictionary<string, string>? KeyBindings { get; init; }

    public static InputConfig Defaults() => new()
    {
        ReaderModifier = "both",
        KeyBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
    };
}
