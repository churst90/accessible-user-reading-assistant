using System.Diagnostics.CodeAnalysis;
using OpenReader.Abstractions.Speech;

namespace OpenReader.Speech.Queue;

/// <summary>
/// Priority queue of <see cref="SpeechUtterance"/>s with cancel-group and
/// coalescing semantics.
/// </summary>
/// <remarks>
/// <para>
/// <b>Priority.</b> Items are dequeued in <see cref="SpeechPriority"/> order:
/// <c>Now</c> first, then <c>Next</c>, then <c>Background</c>. Within a
/// priority, items dequeue in enqueue order.
/// </para>
/// <para>
/// <b>Cancel groups.</b> Enqueueing an utterance with a non-null
/// <see cref="SpeechUtterance.CancelGroup"/> drops any pending utterance with
/// the same group. This is how stale focus speech gets cut when focus moves
/// quickly.
/// </para>
/// <para>
/// <b>Coalesce.</b> Two enqueues of the same group + identical text within
/// <see cref="CoalesceWindow"/> are deduplicated — the second is dropped.
/// </para>
/// <para>
/// <b>Now preemption.</b> Enqueueing a <c>Now</c> drops all pending non-Now
/// items and raises <see cref="PreemptiveEnqueued"/> so the consumer can
/// cancel in-flight engine playback.
/// </para>
/// <para>
/// Thread-safe. <see cref="DequeueAsync"/> may be awaited from one consumer;
/// <see cref="Enqueue"/> may be called from any thread.
/// </para>
/// </remarks>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Domain term for the speech arbitration queue, see docs/SPEECH_PIPELINE.md.")]
public sealed class SpeechQueue : IDisposable
{
    private readonly object _gate = new();
    private readonly LinkedList<SpeechUtterance> _items = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly TimeProvider _time;
    private string? _lastCoalesceGroup;
    private string? _lastCoalesceText;
    private long _lastEnqueueTicks;
    private string? _currentSpeakingGroup;
    private bool _disposed;

    public SpeechQueue(TimeSpan? coalesceWindow = null, TimeProvider? timeProvider = null)
    {
        CoalesceWindow = coalesceWindow ?? TimeSpan.FromMilliseconds(150);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Window during which identical group+text enqueues are deduped.</summary>
    public TimeSpan CoalesceWindow { get; }

    /// <summary>Raised when a <see cref="SpeechPriority.Now"/> item is enqueued.</summary>
    public event Action<SpeechUtterance>? PreemptiveEnqueued;

    /// <summary>Number of pending items.</summary>
    public int Count
    {
        get { lock (_gate) { return _items.Count; } }
    }

    /// <summary>True iff no items are pending.</summary>
    public bool IsEmpty
    {
        get { lock (_gate) { return _items.Count == 0; } }
    }

    /// <summary>
    /// Enqueue an utterance. Returns true if the utterance was queued, false if
    /// it was coalesced away.
    /// </summary>
    public bool Enqueue(SpeechUtterance utterance)
    {
        ArgumentNullException.ThrowIfNull(utterance);

        var preempt = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var nowTicks = _time.GetUtcNow().UtcTicks;
            if (ShouldCoalesce(utterance, nowTicks))
            {
                _lastEnqueueTicks = nowTicks;
                return false;
            }

            var dropped = 0;

            if (utterance.CancelGroup is not null)
            {
                dropped += DropMatching(node => node.Value.CancelGroup == utterance.CancelGroup);
                // If the engine is currently speaking an utterance from this
                // same cancel group (e.g. an old focus-change announcement
                // while the user has just arrowed to a new icon), cut it off
                // so the new utterance starts immediately. This is the
                // "snappy" semantic users expect from screen readers — the
                // current focus must always be heard, not the previous one.
                if (string.Equals(_currentSpeakingGroup, utterance.CancelGroup, StringComparison.Ordinal))
                {
                    preempt = true;
                }
            }

            if (utterance.Priority == SpeechPriority.Now)
            {
                dropped += DropMatching(node => node.Value.Priority < SpeechPriority.Now);
                preempt = true;
            }

            DrainSignal(dropped);

            InsertByPriority(utterance);
            _lastCoalesceGroup = utterance.CancelGroup;
            _lastCoalesceText = utterance.Text;
            _lastEnqueueTicks = nowTicks;
            _signal.Release();
        }

        if (preempt)
        {
            PreemptiveEnqueued?.Invoke(utterance);
        }
        return true;
    }

    /// <summary>
    /// Tell the queue which cancel-group is currently being spoken by the
    /// engine, or <c>null</c> when the engine is idle. The drain loop calls
    /// this around <see cref="OpenReader.Abstractions.Speech.ISpeechEngine.SpeakAsync"/>
    /// so cancel-group enqueues can preempt mid-utterance, not just drop pending items.
    /// </summary>
    public void SetCurrentSpeakingGroup(string? group)
    {
        lock (_gate)
        {
            _currentSpeakingGroup = group;
        }
    }

    /// <summary>Try to dequeue the highest-priority pending item synchronously.</summary>
    public bool TryDequeue(out SpeechUtterance? utterance)
    {
        lock (_gate)
        {
            if (_items.First is null)
            {
                utterance = null;
                return false;
            }
            utterance = _items.First.Value;
            _items.RemoveFirst();
            // consume the matching semaphore release so DequeueAsync's wait stays balanced
            _signal.Wait(0);
            return true;
        }
    }

    /// <summary>Asynchronously wait for and dequeue the next item.</summary>
    public async ValueTask<SpeechUtterance> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_items.First is not null)
                {
                    var u = _items.First.Value;
                    _items.RemoveFirst();
                    return u;
                }
            }
        }
    }

    /// <summary>Synchronous test helper. Block up to <paramref name="timeout"/> for the next item.</summary>
    public SpeechUtterance? WaitForNext(TimeSpan timeout)
    {
        if (!_signal.Wait(timeout))
        {
            return null;
        }
        lock (_gate)
        {
            if (_items.First is null)
            {
                return null;
            }
            var u = _items.First.Value;
            _items.RemoveFirst();
            return u;
        }
    }

    /// <summary>Drop all pending items.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            var n = _items.Count;
            _items.Clear();
            DrainSignal(n);
            _lastCoalesceGroup = null;
            _lastCoalesceText = null;
        }
    }

    /// <summary>
    /// Decrement the signal semaphore exactly <paramref name="count"/> times to
    /// keep it balanced after dropping items. The semaphore should always have
    /// at least <paramref name="count"/> permits (one per dropped item) — we
    /// assert that in DEBUG so a future refactor that desyncs the count is
    /// caught immediately, while still tolerating drift gracefully in release.
    /// </summary>
    private void DrainSignal(int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (!_signal.Wait(0))
            {
                System.Diagnostics.Debug.Assert(false,
                    $"SpeechQueue signal/items invariant violated: expected {count} permits, got {i}.");
                return;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }
        _signal.Dispose();
    }

    private bool ShouldCoalesce(SpeechUtterance utterance, long nowTicks)
    {
        if (utterance.CancelGroup is null)
        {
            return false;
        }
        if (!string.Equals(_lastCoalesceGroup, utterance.CancelGroup, StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.Equals(_lastCoalesceText, utterance.Text, StringComparison.Ordinal))
        {
            return false;
        }
        var elapsed = TimeSpan.FromTicks(nowTicks - _lastEnqueueTicks);
        return elapsed >= TimeSpan.Zero && elapsed < CoalesceWindow;
    }

    private int DropMatching(Predicate<LinkedListNode<SpeechUtterance>> predicate)
    {
        var dropped = 0;
        var node = _items.First;
        while (node is not null)
        {
            var next = node.Next;
            if (predicate(node))
            {
                _items.Remove(node);
                dropped++;
            }
            node = next;
        }
        return dropped;
    }

    private void InsertByPriority(SpeechUtterance utterance)
    {
        var node = _items.First;
        while (node is not null && (int)node.Value.Priority >= (int)utterance.Priority)
        {
            node = node.Next;
        }
        if (node is null)
        {
            _items.AddLast(utterance);
        }
        else
        {
            _items.AddBefore(node, utterance);
        }
    }
}
