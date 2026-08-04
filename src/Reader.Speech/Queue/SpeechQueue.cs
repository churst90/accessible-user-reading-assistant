using System.Diagnostics.CodeAnalysis;
using Aura.Abstractions.Output;
using Aura.Abstractions.Speech;

namespace Aura.Speech.Queue;

/// <summary>
/// Priority queue of <see cref="Utterance"/>s with cancel-group and
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
/// <see cref="Utterance.CancelGroup"/> drops any pending utterance with
/// the same group. This is how stale focus speech gets cut when focus moves
/// quickly.
/// </para>
/// <para>
/// <b>There is no content-based suppression here, deliberately.</b> This class
/// used to drop a second enqueue whose group and text matched the first within
/// a time window. That is the same mistake <see cref="Aura.Output.OutputArbiter"/>
/// had removed from it, made a layer lower: comparing <em>words</em> cannot
/// tell "the provider sent that twice" from "the next thing legitimately reads
/// the same". Arrowing up through consecutive blank lines says "blank" every
/// time, and a toolbar of unnamed icon buttons says "button" every time, and
/// both were being swallowed after the first.
/// </para>
/// <para>
/// Genuine duplicates are already handled twice over, by mechanisms that key on
/// identity rather than content: the arbiter drops two producers describing one
/// action, and the cancel group below drops a superseded announcement about the
/// same kind of thing. A provider re-firing focus for the same element leaves
/// one item queued because of the cancel group, not because of its text.
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
    private readonly LinkedList<Utterance> _items = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly TimeProvider _time;
    private string? _currentSpeakingGroup;
    private SpeechPriority _currentSpeakingPriority = SpeechPriority.Background;
    private bool _disposed;

    public SpeechQueue(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Raised when a <see cref="SpeechPriority.Now"/> item is enqueued.</summary>
    public event Action<Utterance>? PreemptiveEnqueued;

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
    public bool Enqueue(Utterance utterance)
    {
        ArgumentNullException.ThrowIfNull(utterance);

        var preempt = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

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
            else if (_currentSpeakingGroup is not null && utterance.Priority > _currentSpeakingPriority)
            {
                // Something more important than what is playing. Queueing
                // behind it is not good enough: a desktop icon's tooltip is
                // Background and starts speaking the moment it appears, so the
                // icon's own name — Navigation, and the thing the user actually
                // asked for by arrowing — would wait for a sentence of
                // description about a control they have not been told the name
                // of yet.
                preempt = true;
            }

            DrainSignal(dropped);

            InsertByPriority(utterance);
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
    /// this around <see cref="Aura.Abstractions.Speech.ISpeechEngine.SpeakAsync"/>
    /// so cancel-group enqueues can preempt mid-utterance, not just drop pending items.
    /// </summary>
    public void SetCurrentSpeakingGroup(string? group)
        => SetCurrentSpeaking(group, SpeechPriority.Next);

    /// <summary>
    /// Tell the queue what the engine is speaking, or <c>null</c> when idle.
    /// </summary>
    public void SetCurrentSpeaking(string? group, SpeechPriority priority)
    {
        lock (_gate)
        {
            _currentSpeakingGroup = group;
            _currentSpeakingPriority = priority;
        }
    }

    /// <summary>Try to dequeue the highest-priority pending item synchronously.</summary>
    public bool TryDequeue(out Utterance? utterance)
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
    public async ValueTask<Utterance> DequeueAsync(CancellationToken cancellationToken)
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
    public Utterance? WaitForNext(TimeSpan timeout)
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

    /// <summary>
    /// Drop every pending item whose reason has passed. Returns how many went.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call this <b>after</b> the state a predicate reads has been updated —
    /// after the new focus has been recorded, not before — or every predicate
    /// answers about the world as it was and nothing is swept.
    /// </para>
    /// <para>
    /// This is what replaces cancelling speech on every keypress. Cancelling on
    /// input cannot tell a stale announcement from a valid one that happens to
    /// be queued, so it produced both failure modes in turn: speech running an
    /// item behind when it cancelled too little, and silence on backspace when
    /// it cancelled too much. Asking each item whether it is still wanted has
    /// no timing in it, and therefore no race.
    /// </para>
    /// </remarks>
    public int SweepInvalid()
    {
        lock (_gate)
        {
            var dropped = DropMatching(static node => node.Value.Validity is { } v && !v.IsStillValid());
            DrainSignal(dropped);
            return dropped;
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

    private int DropMatching(Predicate<LinkedListNode<Utterance>> predicate)
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

    private void InsertByPriority(Utterance utterance)
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
