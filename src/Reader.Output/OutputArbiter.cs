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
///   <item><b>Identical text repeated immediately → drop.</b> Arrowing at the
///   end of a list re-raises the same event for the same item. Repeating it
///   tells the user nothing; silence tells them they are at the boundary.</item>
/// </list>
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
    private string? _lastText;
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
    /// <param name="text">The composed text, used for the repeat check.</param>
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

                // The user acted once; two producers described it. Keep the
                // one that ranks higher.
                if (sameSubject && category < _lastCategory)
                {
                    return OutputDecision.Drop;
                }

                // The same words again, immediately. At a list boundary this is
                // the control re-raising an event for an item that never moved.
                if (!string.IsNullOrEmpty(text)
                    && string.Equals(_lastText, text, StringComparison.Ordinal)
                    && category != OutputCategory.UserRequested)
                {
                    return OutputDecision.Drop;
                }
            }

            _lastSubject = subject;
            _lastCategory = category;
            _lastText = text;
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
            _lastText = null;
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
