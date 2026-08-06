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
    /// <summary>
    /// A tooltip appeared. Distinct from <see cref="AlertRaised"/> on purpose:
    /// an alert is important enough to interrupt, and a tooltip is not. Sharing
    /// one kind meant a desktop icon's tooltip cut off the icon's own name
    /// half-announced, so the user heard the description of a thing they had
    /// not yet been told the name of.
    /// </summary>
    ToolTipOpened     = 1 << 10,
    /// <summary>
    /// An item joined a multiple selection — Ctrl+Space, or Ctrl+click. Distinct
    /// from <see cref="SelectionChanged"/>, which is a single-selection
    /// container moving its one selection: there the item is the news, here the
    /// news is that it became selected without focus going anywhere.
    /// </summary>
    SelectionAdded    = 1 << 11,
    /// <summary>An item left a multiple selection.</summary>
    SelectionRemoved  = 1 << 12,
    All              = ~0,
}

/// <summary>
/// A single accessibility event. The node may be null for events whose source
/// has already gone away by the time the event was dispatched.
/// </summary>
/// <remarks>
/// <para>
/// <c>CaretLine</c> carries text the provider captured at event time, when the
/// only safe moment to read it was inside the handler.
/// </para>
/// <para>
/// There were two more fields here — <c>CharBeforeCaret</c> and
/// <c>SelectionText</c> — with a long comment explaining that the first existed
/// so Backspace could announce the character it deletes. Nothing ever populated
/// or read either of them. The comment described a mechanism that did not
/// exist, which is worse than no comment: it answered the question "how does
/// deletion know what vanished?" wrongly, for anyone who looked. Deletion is
/// answered by <c>CaretTracker.CharBefore</c>, which captures the neighbours on
/// samples that were happening anyway.
/// </para>
/// </remarks>
public sealed record AccessibilityEvent(
    AccessibilityEventKind Kind,
    AccessibleNode? Node,
    DateTimeOffset At,
    string? CaretLine = null);
