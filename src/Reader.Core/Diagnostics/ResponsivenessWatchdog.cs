namespace Aura.Core.Diagnostics;

/// <summary>
/// Notices when the reader has gone silent while the user is still driving it,
/// and says so.
/// </summary>
/// <remarks>
/// <para>
/// A screen reader that stops speaking is in its worst possible state. A
/// sighted user sees a hung application and works around it; a blind user gets
/// no signal at all — no error, no sound, no indication of whether the reader
/// died, the application froze, or the keyboard stopped working. The usual
/// recovery is a reboot or sighted help.
/// </para>
/// <para>
/// The most common cause is a cross-process call into an application that has
/// stopped pumping messages. <c>Win32Text</c> bounds the Win32 side with
/// <c>SendMessageTimeout</c>, but the managed UIA client exposes no timeout at
/// all, so a wedged provider can still stall the reader. This watchdog does
/// not prevent that; it converts a silent hang into an audible, diagnosable
/// one, which is the difference between "my computer is broken" and "this app
/// is not responding".
/// </para>
/// <para>
/// Deliberately dumb: it knows only that input arrived and that speech did or
/// did not follow. It cannot tell a hung provider from a genuinely silent
/// keystroke, which is why <see cref="NotifyInput"/> should be called only for
/// input that is <em>expected</em> to produce speech.
/// </para>
/// </remarks>
public sealed class ResponsivenessWatchdog : IDisposable
{
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private ITimer? _timer;
    private long _pendingSinceTicks;
    private bool _pending;
    private bool _reported;
    private bool _disposed;

    public ResponsivenessWatchdog(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// How long speech may lag input before the reader declares itself stalled.
    /// </summary>
    /// <remarks>
    /// Generous on purpose. The target for the speech path is under 50 ms, but
    /// this is not a latency alarm — it is a liveness alarm, and a false
    /// "not responding" is its own kind of harm. Two seconds is far past any
    /// legitimate delay and far short of the user giving up.
    /// </remarks>
    public TimeSpan StallThreshold { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Raised once per stall, with how long the reader has been unresponsive.
    /// The host is expected to play an earcon and log; speech itself may be
    /// exactly what is broken, so the cue must not depend on it.
    /// </summary>
    public event Action<TimeSpan>? Stalled;

    /// <summary>Raised when speech resumes after a reported stall.</summary>
    public event Action? Recovered;

    /// <summary>Start polling. Idempotent.</summary>
    public void Start(TimeSpan? pollInterval = null)
    {
        lock (_gate)
        {
            if (_timer is not null || _disposed)
            {
                return;
            }
            var interval = pollInterval ?? TimeSpan.FromMilliseconds(500);
            _timer = _time.CreateTimer(static state => ((ResponsivenessWatchdog)state!).Poll(), this, interval, interval);
        }
    }

    /// <summary>Input arrived that should produce speech.</summary>
    public void NotifyInput()
    {
        lock (_gate)
        {
            if (_pending)
            {
                // Already waiting on an earlier keystroke; keep the older
                // timestamp so a burst of keys doesn't keep resetting the
                // clock and hide a stall indefinitely.
                return;
            }
            _pending = true;
            _pendingSinceTicks = _time.GetTimestamp();
        }
    }

    /// <summary>Speech was produced. Clears any outstanding expectation.</summary>
    public void NotifyOutput()
    {
        bool recovered;
        lock (_gate)
        {
            _pending = false;
            recovered = _reported;
            _reported = false;
        }
        if (recovered)
        {
            Recovered?.Invoke();
        }
    }

    /// <summary>
    /// Evaluate liveness once. Called by the timer; exposed so tests can drive
    /// it deterministically rather than sleeping.
    /// </summary>
    public void Poll()
    {
        TimeSpan elapsed;
        lock (_gate)
        {
            if (!_pending || _reported || _disposed)
            {
                return;
            }
            elapsed = _time.GetElapsedTime(_pendingSinceTicks);
            if (elapsed < StallThreshold)
            {
                return;
            }
            // Report once per stall. Repeating the cue every poll while an app
            // is frozen would be its own denial of service.
            _reported = true;
        }
        Stalled?.Invoke(elapsed);
    }

    public void Dispose()
    {
        ITimer? timer;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            timer = _timer;
            _timer = null;
        }
        timer?.Dispose();
    }
}
