using Aura.Abstractions.Speech;

namespace Aura.Output;

/// <summary>What should happen to an announcement.</summary>
public enum OutputDecision
{
    /// <summary>Speak it.</summary>
    Speak,

    /// <summary>Drop it — something equivalent or more important already covers it.</summary>
    Drop,
}

/// <summary>
/// The single place that decides whether an announcement is spoken.
/// </summary>
/// <remarks>
/// <para>
/// Every producer submits here, and nothing reaches the speech queue without
/// passing through. That is the point: precedence between producers is a
/// property of the system, not something each pair of components negotiates
/// privately.
/// </para>
/// <para>
/// Two rules, and deliberately only two:
/// </para>
/// <list type="number">
///   <item><b>Same subject, lower category, close in time → drop.</b> A list
///   raises focus <em>and</em> selection for one arrow press; the caret moves
///   and both a keystroke sample and a UIA event describe it. The user
///   performed one action and should hear one announcement.</item>
/// </list>
/// <para>
/// There was briefly a second rule — drop identical text repeated immediately —
/// meant to silence a list boundary. It was wrong, and wrong in the dangerous
/// direction: arrowing up through consecutive blank lines in a document
/// produces the same word ("blank") each time, legitimately, and the rule
/// swallowed every one after the first. Suppressing on <em>content</em> cannot
/// tell "nothing moved" from "the next thing happens to read the same", so it
/// does not belong here. <c>SpeechQueue</c> already coalesces genuine
/// duplicates within a tighter, cancel-group-aware window, and a keypress now
/// cancels in-flight speech, which is what actually produces silence at a
/// boundary.
/// </para>
/// <para>
/// Everything else is left to the queue, which already handles priority,
/// cancel groups and preemption. This class decides <em>whether</em> to speak;
/// the queue decides <em>when</em>, and the engine decides how. Keeping those
/// three separate is what stopped the last round of bugs from being fixable
/// only by adding another timer.
/// </para>
/// <para>
/// Not thread-safe by accident: <see cref="Evaluate"/> locks, because
/// producers submit from the UIA dispatch loop, the keyboard hook and the
/// thread pool simultaneously.
/// </para>
/// </remarks>
public sealed class OutputArbiter
{
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    private string? _lastSubject;
    private OutputCategory _lastCategory;
    private SpeechReason _lastReason;
    private long _lastAtTicks;
    private bool _hasLast;

    public OutputArbiter(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// How close in time two announcements must be for the arbiter to treat
    /// them as describing the same user action.
    /// </summary>
    /// <remarks>
    /// Short on purpose. This is not a debounce hiding a race — the producers
    /// it arbitrates fire within a few milliseconds of each other because they
    /// are reacting to one event. A long window would start swallowing
    /// genuinely separate announcements, which is the worse failure.
    /// </remarks>
    public TimeSpan CoincidenceWindow { get; set; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Decide whether <paramref name="request"/> should be spoken.</summary>
    /// <param name="request">The announcement.</param>
    /// <param name="subject">
    /// What it is about — normally the node id. Announcements about different
    /// subjects never suppress each other, however close together they arrive.
    /// </param>
    /// <param name="text">The composed text. Retained for future rules and logging.</param>
    public OutputDecision Evaluate(SpeechRequest request, string? subject, string? text)
    {
        ArgumentNullException.ThrowIfNull(request);

        var category = Categorize(request.Reason);
        var now = _time.GetTimestamp();

        lock (_gate)
        {
            if (_hasLast && _time.GetElapsedTime(_lastAtTicks) < CoincidenceWindow)
            {
                var sameSubject = subject is not null
                    && string.Equals(_lastSubject, subject, StringComparison.Ordinal);

                // Two DIFFERENT reasons about the same subject, moments apart,
                // means two producers described one user action — a list
                // raising focus and selection for a single arrow press. Keep
                // whichever ranks higher.
                //
                // The same reason twice is the opposite case: two real actions
                // that happen to look alike, such as arrowing through
                // consecutive blank lines. Those must both be heard, which is
                // why this compares reasons rather than content.
                if (sameSubject
                    && request.Reason != _lastReason
                    && category <= _lastCategory)
                {
                    return OutputDecision.Drop;
                }
            }

            _lastSubject = subject;
            _lastCategory = category;
            _lastReason = request.Reason;
            _lastAtTicks = now;
            _hasLast = true;
        }

        return OutputDecision.Speak;
    }

    /// <summary>
    /// Forget the last announcement. Call when the user moves somewhere new,
    /// so returning to a control still announces it.
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            _hasLast = false;
            _lastSubject = null;
        }
    }

    /// <summary>Map a reason onto what it may interrupt.</summary>
    public static OutputCategory Categorize(SpeechReason reason) => reason switch
    {
        // The user pressed a key specifically to hear these.
        SpeechReason.ReadCharacter or SpeechReason.ReadWord or SpeechReason.ReadLine
            or SpeechReason.ReadAll or SpeechReason.ReviewMove
            or SpeechReason.UserAnnouncement => OutputCategory.UserRequested,

        SpeechReason.CaretMoved => OutputCategory.Navigation,
        SpeechReason.FocusChanged => OutputCategory.Navigation,
        SpeechReason.SelectionChanged => OutputCategory.Navigation,

        SpeechReason.ValueChanged => OutputCategory.StateChange,

        SpeechReason.AlertRaised or SpeechReason.LiveRegionUpdate => OutputCategory.Ambient,

        // An unmapped reason must not outrank a real one, but must still be
        // heard — Ambient is the safe floor.
        _ => OutputCategory.Ambient,
    };
}
