namespace OpenReader.Abstractions.Input;

/// <summary>
/// Modifier keys held while another key was pressed. Multi-flag.
/// </summary>
[Flags]
public enum InputModifiers
{
    None    = 0,
    Shift   = 1 << 0,
    Control = 1 << 1,
    Alt     = 1 << 2,
    Win     = 1 << 3,
    /// <summary>The configured screen-reader modifier (typically Insert or CapsLock).</summary>
    Reader  = 1 << 4,
}

/// <summary>Source of a raw input event.</summary>
public enum InputSource
{
    Keyboard = 0,
    BrailleDisplay,
    Touch,
    Synthetic,
}

/// <summary>Whether an input event is the press, release, or repeat of a key.</summary>
public enum InputEventKind
{
    KeyDown = 0,
    KeyUp,
    KeyRepeat,
    BrailleRouting,
    TouchTap,
}

/// <summary>
/// A single low-level input event before gesture mapping. Produced by
/// <see cref="IInputSource"/>; consumed by the gesture/command layer.
/// </summary>
public sealed record RawInput(
    InputSource Source,
    InputEventKind Kind,
    int KeyCode,
    InputModifiers Modifiers,
    DateTimeOffset At,
    IReadOnlyDictionary<string, object?>? Extras = null);
