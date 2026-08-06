using Aura.Abstractions.Output;

namespace Aura.Speech.Rendering;

/// <summary>
/// Which parts of an announcement are worth hearing.
/// </summary>
/// <remarks>
/// <para>
/// This is what <see cref="SegmentKind"/> was for. Announcements are composed
/// from segments that each say what they <em>are</em> — the control's name, its
/// role, where it sits in a list — so "stop telling me it is a list item" is
/// one filter over a list rather than a configuration check inside every rule.
/// In NVDA the same choices are scattered through <c>speech.py</c> as
/// <c>formatConfig</c> tests at each call site, which is why they cannot be
/// changed without editing code.
/// </para>
/// <para>
/// <see cref="SegmentKind.Content"/> and <see cref="SegmentKind.Name"/> are
/// deliberately not listed. They are the thing itself; a reader that can be
/// configured to stop saying what it is looking at has no remaining purpose.
/// </para>
/// </remarks>
public sealed record Verbosity
{
    /// <summary>Say what kind of control it is — "button", "list item".</summary>
    public bool ReportRole { get; init; } = true;

    /// <summary>Say where it sits — "4 of 10", "level 2".</summary>
    public bool ReportPosition { get; init; } = true;

    /// <summary>Say its state — "checked", "expanded", "read only".</summary>
    public bool ReportState { get; init; } = true;

    /// <summary>Say its description or help text.</summary>
    public bool ReportDescription { get; init; } = true;

    /// <summary>Say how to operate it — "press space to activate".</summary>
    public bool ReportHints { get; init; }

    /// <summary>Everything on. The default.</summary>
    public static Verbosity Full => new();

    /// <summary>True when a segment of this kind should be conveyed.</summary>
    public bool Allows(SegmentKind kind) => kind switch
    {
        SegmentKind.Role => ReportRole,
        SegmentKind.Position => ReportPosition,
        SegmentKind.State => ReportState,
        SegmentKind.Description => ReportDescription,
        SegmentKind.Hint => ReportHints,
        _ => true,
    };
}
