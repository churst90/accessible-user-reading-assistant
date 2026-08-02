namespace Aura.Speech;

/// <summary>
/// Lightweight shared timestamp marking the most recent typing keystroke.
/// </summary>
/// <remarks>
/// <para>
/// Used by the speech pipeline to suppress automatic <c>ValueChanged</c>
/// announcements on text-input controls (edits, comboboxes) while the user
/// is actively typing — without this, the Run dialog and similar editable
/// fields read back the growing value on every keystroke ("n", "no", "not",
/// "note", "notep", "notepa", "notepad") instead of the user's preferred
/// character or word echo.
/// </para>
/// <para>
/// The host wires <see cref="NotifyTyping"/> to fire from the keyboard hook
/// on every printable keystroke and on Backspace. The pipeline reads
/// <see cref="IsTyping"/> when deciding whether to emit a speech utterance
/// for a value change.
/// </para>
/// <para>
/// Thread-safe. Reads and writes are atomic on a 64-bit ticks field; no lock
/// needed.
/// </para>
/// </remarks>
public sealed class TypingState
{
    private long _lastTypingTicks;

    /// <summary>How long after the last keystroke ValueChanged stays muted.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Mark "user typed something just now."</summary>
    public void NotifyTyping()
    {
        Volatile.Write(ref _lastTypingTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    /// <summary>True if a typing keystroke fell within <see cref="Window"/>.</summary>
    public bool IsTyping
    {
        get
        {
            var last = Volatile.Read(ref _lastTypingTicks);
            if (last == 0)
            {
                return false;
            }
            var elapsed = DateTimeOffset.UtcNow.UtcTicks - last;
            return elapsed >= 0 && TimeSpan.FromTicks(elapsed) < Window;
        }
    }
}
