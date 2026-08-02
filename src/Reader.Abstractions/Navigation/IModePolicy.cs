using OpenReader.Abstractions.Accessibility;

namespace OpenReader.Abstractions.Navigation;

/// <summary>
/// Decides which mode the reader should be in for a given focus target.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the mode manager because this is the part users argue about,
/// and the part that needs per-site and per-app overrides. Whether an edit box
/// should auto-switch to <see cref="ReaderMode.Type"/> has no universally
/// right answer: it does in a search field, and it does not in a rich-text
/// editor where the user is proof-reading.
/// </para>
/// <para>
/// Keeping the policy behind an interface means those arguments are settled in
/// configuration and plugins rather than in the dispatch loop. This is the
/// specific mechanism for "app quirks never live in core".
/// </para>
/// </remarks>
public interface IModePolicy
{
    /// <summary>
    /// The mode to use when focus lands on <paramref name="node"/>, or
    /// <c>null</c> to leave the current mode alone.
    /// </summary>
    /// <remarks>
    /// Returning <c>null</c> rather than a default matters: most focus changes
    /// should not disturb the mode, and a policy that answers every question
    /// will fight the user's manual toggle.
    /// </remarks>
    ReaderMode? ModeFor(AccessibleNode node, ReaderMode current);

    /// <summary>
    /// Whether the user's manual toggle should stick for this node, rather
    /// than being re-decided on the next focus change.
    /// </summary>
    /// <remarks>
    /// The behaviour that makes automatic mode switching bearable. Without it,
    /// a user who deliberately switches to Read mode inside a text editor is
    /// thrown straight back out by the next focus event, which reads as the
    /// reader fighting them.
    /// </remarks>
    bool RespectsManualOverride(AccessibleNode node);
}
