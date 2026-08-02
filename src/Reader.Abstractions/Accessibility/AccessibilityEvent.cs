namespace Aura.Abstractions.Accessibility;

/// <summary>
/// Discriminator for the kind of event raised by an accessibility provider.
/// </summary>
[Flags]
public enum AccessibilityEventKind
{
    None             = 0,
    FocusChanged     = 1 << 0,
    NameChanged      = 1 << 1,
    ValueChanged     = 1 << 2,
    StateChanged     = 1 << 3,
    SelectionChanged = 1 << 4,
    StructureChanged = 1 << 5,
    Activated        = 1 << 6,
    AlertRaised      = 1 << 7,
    LiveRegionChanged = 1 << 8,
    /// <summary>The text caret moved inside an editable / read-only document.</summary>
    CaretMoved        = 1 << 9,
    All              = ~0,
}

/// <summary>
/// A single accessibility event. The node may be null for events whose source
/// has already gone away by the time the event was dispatched. For
/// <see cref="AccessibilityEventKind.CaretMoved"/>, <c>CaretLine</c> carries
/// the line under the caret at the moment of the event, <c>CharBeforeCaret</c>
/// the single character immediately to the left (used so Backspace can announce
/// the character it deletes — by the time we observe the key event the OS has
/// already dispatched the deletion, so we read this on the cleaner side and
/// cache it), and <c>SelectionText</c> the current selection if any.
/// </summary>
public sealed record AccessibilityEvent(
    AccessibilityEventKind Kind,
    AccessibleNode? Node,
    DateTimeOffset At,
    string? CaretLine = null,
    string? CharBeforeCaret = null,
    string? SelectionText = null);
