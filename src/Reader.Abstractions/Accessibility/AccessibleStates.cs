namespace Aura.Abstractions.Accessibility;

/// <summary>
/// Bitfield of states that may apply to an accessibility node.
/// </summary>
[Flags]
public enum AccessibleStates : ulong
{
    None = 0,

    Focused      = 1UL << 0,
    Focusable    = 1UL << 1,
    Selected     = 1UL << 2,
    Selectable   = 1UL << 3,
    Checked      = 1UL << 4,
    Mixed        = 1UL << 5,
    Pressed      = 1UL << 6,
    Expanded     = 1UL << 7,
    Collapsed    = 1UL << 8,
    Expandable   = 1UL << 9,
    Busy         = 1UL << 10,
    Disabled     = 1UL << 11,
    ReadOnly     = 1UL << 12,
    Required     = 1UL << 13,
    Invalid      = 1UL << 14,
    Visited      = 1UL << 15,
    Offscreen    = 1UL << 16,
    Invisible    = 1UL << 17,
    Modal        = 1UL << 18,
    Multiline    = 1UL << 19,
    MultiSelect  = 1UL << 20,
    Protected    = 1UL << 21,
    HasPopup     = 1UL << 22,
    Default      = 1UL << 23,
    Editable     = 1UL << 24,
}
