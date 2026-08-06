using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Output;

namespace Aura.Core.Output;

/// <summary>
/// Remembers what has focus, so a queued announcement can ask whether it is
/// still describing the thing the user is looking at.
/// </summary>
/// <remarks>
/// <para>
/// One writer — the dispatch loop, on every focus change — and many readers, on
/// the speech thread. That asymmetry is the point: the announcement does not
/// have to be told it has gone stale, it finds out by asking.
/// </para>
/// </remarks>
public sealed class FocusTracker
{
    private readonly object _gate = new();
    private string? _currentId;
    private string? _currentWindowId;
    private List<string> _ancestorIds = [];

    /// <summary>The node id that currently has focus, if any.</summary>
    public string? CurrentId
    {
        get { lock (_gate) { return _currentId; } }
    }

    /// <summary>
    /// Record a focus change. Call this <b>before</b> sweeping the speech queue
    /// — a predicate evaluated against the old state answers about a world that
    /// no longer exists, and nothing gets swept.
    /// </summary>
    /// <param name="focused">The newly focused node.</param>
    /// <param name="ancestorIds">
    /// Its ancestors, outermost first, when known. Used so an announcement
    /// about a container that the focus has just moved *into* still counts as
    /// current — the user is being told where they are, and that is not stale.
    /// </param>
    /// <param name="windowId">The id of the owning top-level window, when known.</param>
    public void OnFocusChanged(AccessibleNode? focused, IReadOnlyList<string>? ancestorIds = null, string? windowId = null)
    {
        lock (_gate)
        {
            _currentId = focused?.Id.Value;
            _currentWindowId = windowId;
            _ancestorIds = ancestorIds is null ? [] : [.. ancestorIds];
        }
    }

    /// <summary>
    /// A predicate for an announcement about <paramref name="subject"/>.
    /// </summary>
    public IValidityPredicate For(string? subject) => new FocusStillCurrent(this, subject);

    private bool IsStillCurrent(string? subject)
    {
        if (subject is null)
        {
            return true;
        }
        lock (_gate)
        {
            // Still the focus. The ordinary case.
            if (string.Equals(_currentId, subject, StringComparison.Ordinal))
            {
                return true;
            }
            // There used to be an exemption here for anything that had never
            // held focus, meant to protect alerts and toasts from being swept.
            // It was both unnecessary and harmful. Unnecessary because those
            // announcements are never given a predicate in the first place —
            // only focus and selection are. Harmful because a focus event that
            // the provider deduped away never reached the tracker, so a stale
            // announcement about that element looked like "never focused" and
            // was spoken: press Down in a list and hear the item you just left,
            // then the one you are on.
            //
            // A focus or selection announcement about something that is not the
            // focus is stale. There is no case where it is not.

            // An ancestor of what now has focus: the user moved inward, and
            // "dialog, Save" wants both halves.
            if (_ancestorIds.Contains(subject, StringComparer.Ordinal))
            {
                return true;
            }
            // The owning window. A dialog's title has to be heard even though
            // focus lands on a control inside it.
            if (string.Equals(_currentWindowId, subject, StringComparison.Ordinal))
            {
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// "Is the thing this announcement describes still what the user is looking
    /// at?", asked at the moment it would be spoken.
    /// </summary>
    private sealed class FocusStillCurrent(FocusTracker tracker, string? subject) : IValidityPredicate
    {
        public bool IsStillValid() => tracker.IsStillCurrent(subject);

        public override string ToString() => $"focus still on {subject ?? "(none)"}";
    }
}
