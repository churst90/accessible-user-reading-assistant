namespace Aura.Input.Gestures;

/// <summary>
/// Tracks the last fire time of selected commands and reports whether a
/// repeat fell inside the configured window. Used to convert
/// <c>ReportTime → ReportDate</c> on double-tap of Reader+F12 and similar.
/// </summary>
/// <remarks>
/// State is keyed by command id, so different double-tap-able commands don't
/// interfere. Calling <see cref="Observe"/> always updates the timestamp;
/// callers map a true return to the alternate behavior.
/// </remarks>
public sealed class DoubleTapDetector
{
    private readonly object _gate = new();
    private readonly Dictionary<int, long> _lastTicks = new();
    private readonly long _windowTicks;

    public DoubleTapDetector(TimeSpan? window = null)
    {
        _windowTicks = (window ?? TimeSpan.FromMilliseconds(450)).Ticks;
    }

    /// <summary>
    /// Observe a press of <paramref name="key"/>. Returns <c>true</c> if this
    /// press happened within the double-tap window of the previous one for the
    /// same key.
    /// </summary>
    public bool Observe(int key)
    {
        var now = DateTime.UtcNow.Ticks;
        lock (_gate)
        {
            var doubled = _lastTicks.TryGetValue(key, out var previous) && (now - previous) < _windowTicks;
            // Reset on double so a third quick press starts a new pair.
            _lastTicks[key] = doubled ? 0 : now;
            return doubled;
        }
    }
}
