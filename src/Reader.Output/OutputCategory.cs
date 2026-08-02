namespace Aura.Output;

/// <summary>
/// What kind of thing an announcement is, and therefore what it may interrupt.
/// </summary>
/// <remarks>
/// <para>
/// Order is significance, low to high. A higher category may supersede a lower
/// one; a lower one never silences a higher one.
/// </para>
/// <para>
/// This exists because the reader has many independent producers — focus
/// events, selection events, caret sampling, key echo, live regions, plugins,
/// direct command results — and until now each submitted straight to the queue
/// with no one deciding precedence. Every duplicate-speech bug found on real
/// hardware was a collision between two producers describing the same user
/// action: focus and selection both announcing a list item, the caret tracker
/// and the UIA event both announcing a caret move.
/// </para>
/// <para>
/// Those were each patched at the source. Patching at the source does not
/// scale — the next producer collides with the ones already there, and the
/// suppression logic ends up spread across the components that happen to know
/// about each other. Ranking them in one place is the difference between a
/// rule and a workaround.
/// </para>
/// </remarks>
public enum OutputCategory
{
    /// <summary>
    /// Background context nobody asked for: live regions, notifications,
    /// toasts. Never interrupts anything.
    /// </summary>
    Ambient = 0,

    /// <summary>
    /// Something changed about a control without the user moving: a checkbox
    /// toggled, a value updated, a progress bar advanced.
    /// </summary>
    StateChange = 1,

    /// <summary>
    /// The user moved somewhere: focus changed, selection moved, the caret
    /// moved. Supersedes state chatter about wherever they just left.
    /// </summary>
    Navigation = 2,

    /// <summary>
    /// Direct feedback for a keystroke — character and word echo, the
    /// character a deletion removed. Must not be starved by navigation
    /// announcements, because the user is typing and needs the confirmation
    /// now.
    /// </summary>
    Echo = 3,

    /// <summary>
    /// The user explicitly asked: read the line, report focus, what is the
    /// time. Never suppressed, never superseded — the user pressed a key
    /// specifically to hear this.
    /// </summary>
    UserRequested = 4,
}
