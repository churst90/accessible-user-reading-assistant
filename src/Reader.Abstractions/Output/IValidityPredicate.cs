namespace Aura.Abstractions.Output;

/// <summary>
/// Answers "is this announcement still worth conveying?" at the moment it would
/// be conveyed, rather than when it was composed.
/// </summary>
/// <remarks>
/// <para>
/// This is the fix for a whole class of bug, and it is worth being explicit
/// about why, because the wrong fix has been attempted several times.
/// </para>
/// <para>
/// The question "is this announcement still wanted?" looks like a timing
/// question and is not. Answering it with timing — cancel on keypress, exclude
/// the arrow keys, put them back, make the cancel synchronous so it cannot
/// race, add a window in which duplicate text is suppressed — produces two
/// opposite symptoms and no stable middle: cancel too little and the user hears
/// the item they just left; cancel too much and legitimate announcements go
/// silent.
/// </para>
/// <para>
/// It is a <em>state</em> question. An announcement about a control that no
/// longer has focus is stale no matter how recently it was queued, and an
/// announcement about the control that still has focus is wanted no matter how
/// long it waited. Evaluating that at speak time makes stale announcements
/// evaporate and valid ones survive, with no timing involved and therefore no
/// race to lose.
/// </para>
/// <para>
/// Implementations must be cheap and must not block: this is called on the
/// speech path, possibly for every queued item, on every focus change.
/// </para>
/// </remarks>
public interface IValidityPredicate
{
    /// <summary>False when the reason for this announcement has passed.</summary>
    bool IsStillValid();
}
