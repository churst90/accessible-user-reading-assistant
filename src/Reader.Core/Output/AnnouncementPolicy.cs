using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Output;
using Aura.Abstractions.Speech;

namespace Aura.Core.Output;

/// <summary>
/// The rules about <em>which</em> announcements survive: what makes one stale,
/// and in what order the reader must learn things for that question to have a
/// correct answer.
/// </summary>
/// <remarks>
/// <para>
/// This existed twice — once in the host's wiring and once, copied, in the
/// transcript harness — and the copies drifted. The host asked a focus question
/// about selection announcements, every one was swept, and lists went silent;
/// the harness asked the same wrong question, agreed, and the suite built
/// specifically to catch that class of bug reported success. Two implementations
/// of one policy cannot disagree if there is only one.
/// </para>
/// <para>
/// It is deliberately small and platform-free. Everything it needs to know
/// arrives through <see cref="OnFocusChanged"/>; everything it decides comes
/// back through <see cref="ValidityFor"/>. It holds no provider, no queue and
/// no engine, which is what lets a test drive it directly.
/// </para>
/// </remarks>
public sealed class AnnouncementPolicy
{
    private readonly FocusTracker _focus = new();

    /// <summary>What currently has focus, for diagnostics.</summary>
    public string? FocusedId => _focus.CurrentId;

    /// <summary>
    /// Record that focus has moved, and report how many queued announcements
    /// that made stale.
    /// </summary>
    /// <param name="focused">The newly focused node.</param>
    /// <param name="sweep">
    /// The queue's sweep. Called <em>after</em> the new focus is recorded,
    /// which is the entire subtlety: a predicate evaluated first answers about
    /// a world that no longer exists and nothing is dropped.
    /// </param>
    public int OnFocusChanged(AccessibleNode? focused, Func<int> sweep)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        _focus.OnFocusChanged(focused);
        return sweep();
    }

    /// <summary>
    /// The predicate an announcement should carry, or <c>null</c> when it
    /// should never be reconsidered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Focus announcements only. Three cases have to stay distinguishable and
    /// each was learned the hard way:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Focus</b> is about the focused element by definition, so
    ///   "is that still the focus?" is exactly the right question.</item>
    ///   <item><b>Selection</b> is not. A list box keeps keyboard focus on the
    ///   list while the arrows move the selection, so the item being announced
    ///   is never the focused element — asking made every list silent. Its
    ///   staleness is answered by the selection cancel group, where a newer
    ///   selection supersedes a pending older one.</item>
    ///   <item><b>Everything else</b> — alerts, toasts, live regions, anything
    ///   the user pressed a key to hear — carries no predicate at all. An alert
    ///   fires on something that by definition does not have focus, and silence
    ///   in answer to a keystroke is never right.</item>
    /// </list>
    /// </remarks>
    public IValidityPredicate? ValidityFor(SpeechRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Reason == SpeechReason.FocusChanged
            ? _focus.For(request.Node?.Id.Value)
            : null;
    }
}
