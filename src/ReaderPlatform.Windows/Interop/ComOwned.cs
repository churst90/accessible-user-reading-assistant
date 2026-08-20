using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Aura.Platform.Windows.Interop;

/// <summary>
/// Owns a COM interface reference with an explicit lifetime. Release is
/// deferred to a <see cref="ComReleaseQueue"/> and happens at a point in the
/// program we choose, on a thread we control.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is deliberately no finalizer.</b> An instance that is never
/// disposed leaks a bounded amount of memory; an instance released by the
/// finalizer thread can freeze the whole reader for an unbounded time. Those
/// are not comparable costs, so the type is built to make the second one
/// impossible rather than to make the first one unlikely.
/// </para>
/// <para>
/// This is NVDA issue #11398, inherited. Their root cause was Python's garbage
/// collector calling <c>Release()</c> on COM proxies "at random points in
/// random threads"; that <c>Release()</c> is an RPC into the provider process,
/// and if the provider is itself blocked, the releasing thread blocks with it.
/// Their fix was to switch automatic collection off entirely and collect at a
/// known point in the main loop. .NET has the identical hazard under a
/// different name: an unreachable runtime callable wrapper is released by the
/// finalizer thread, which AURA neither owns nor schedules.
/// </para>
/// <para>
/// The symptom, if we get this wrong, is the reader going silent for no reason
/// that any log will explain, rarely, and never twice the same way.
/// </para>
/// </remarks>
/// <typeparam name="T">The COM interface type held.</typeparam>
internal sealed class ComOwned<T> : IDisposable
    where T : class
{
    private readonly ComReleaseQueue _queue;
    private T? _value;

    internal ComOwned(T value, ComReleaseQueue queue)
    {
        _value = value;
        _queue = queue;
    }

    /// <summary>The interface reference. Throws once disposed.</summary>
    public T Value =>
        _value ?? throw new ObjectDisposedException(nameof(ComOwned<T>));

    /// <summary>The interface reference, or <c>null</c> once disposed.</summary>
    public T? ValueOrNull => _value;

    /// <summary>True while this owner still holds the reference.</summary>
    public bool IsAlive => _value is not null;

    /// <summary>
    /// Move the reference to a new owner and empty this one.
    /// </summary>
    /// <remarks>
    /// A move, never a copy, and the distinction is load-bearing. The runtime
    /// hands back the <em>same</em> wrapper when one COM identity is marshalled
    /// into an apartment twice, and each marshal must be matched by exactly one
    /// release. Two owners of one reference would release it twice and the
    /// second call would be operating on something already handed back.
    /// <para>
    /// Used where an element arrives owned by one scope and has to outlive it —
    /// a focus element that arrives on the event queue and becomes the
    /// remembered focus.
    /// </para>
    /// </remarks>
    public ComOwned<T> Transfer()
    {
        var v = Interlocked.Exchange(ref _value, null)
            ?? throw new ObjectDisposedException(nameof(ComOwned<T>));
        // The live count is unchanged: one reference, one owner, new hands.
        return new ComOwned<T>(v, _queue);
    }

    /// <summary>
    /// Hand the reference to the release queue. Safe to call more than once;
    /// only the first call does anything.
    /// </summary>
    public void Dispose()
    {
        var v = Interlocked.Exchange(ref _value, null);
        if (v is not null)
        {
            _queue.Enqueue(v);
        }
    }
}

/// <summary>
/// The known point in the loop where COM references are released.
/// </summary>
/// <remarks>
/// <para>
/// Disposal enqueues; it does not release. The queue is drained by the thread
/// that runs the dispatch loop, between events — so a release that blocks on a
/// wedged provider blocks a thread whose job is already "wait for the next
/// event", rather than the finalizer thread, which everything else in the
/// process eventually depends on.
/// </para>
/// <para>
/// <b>Why <see cref="Marshal.ReleaseComObject"/> and not
/// <c>FinalReleaseComObject</c>.</b> The obvious-looking call is the wrong one.
/// <c>FinalReleaseComObject</c> drops the wrapper's count to zero however many
/// managed holders there are, and the runtime caches one wrapper per COM
/// identity per apartment — so a "final" release of an element that some other
/// part of the reader is still holding would hand that holder an
/// <see cref="InvalidComObjectException"/> on its next call. One release per
/// reference received is the balanced accounting, and it is what this does.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class ComReleaseQueue
{
    private readonly ConcurrentQueue<object> _pending = new();
    private readonly Func<object, int> _release;
    private int _live;
    private long _released;

    /// <summary>Production queue: releases through the COM interop layer.</summary>
    public ComReleaseQueue()
        : this(DefaultRelease)
    {
    }

    /// <summary>
    /// Test seam. The release action is injected so the queue's accounting can
    /// be driven without a COM object in sight.
    /// </summary>
    internal ComReleaseQueue(Func<object, int> release)
    {
        _release = release;
    }

    /// <summary>
    /// References currently owned and not yet handed back. A count that climbs
    /// and never falls is a leak; a count that falls between events is the
    /// queue doing its job. Surfaced in the diagnostic snapshot for exactly
    /// that reason — this is the number that tells you which one is happening.
    /// </summary>
    public int LiveCount => Volatile.Read(ref _live);

    /// <summary>References disposed but not yet released.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Total references released over the life of the process.</summary>
    public long ReleasedCount => Interlocked.Read(ref _released);

    /// <summary>Take ownership of a reference the caller has just received.</summary>
    public ComOwned<T> Own<T>(T value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        Interlocked.Increment(ref _live);
        return new ComOwned<T>(value, this);
    }

    /// <summary>
    /// Take ownership of a reference that may not exist, so callers can wrap a
    /// nullable COM return without a null check at every site.
    /// </summary>
    public ComOwned<T>? OwnIfNotNull<T>(T? value)
        where T : class
        => value is null ? null : Own(value);

    /// <summary>
    /// Take an <em>independent</em> reference to something somebody else owns,
    /// for a caller that needs it to outlive the owner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needed because ownership here is a count, not a flag. The wrapper for one
    /// COM identity is shared, so a second holder that merely copies the
    /// reference is not a second owner — it is a second release of the first
    /// owner's reference, and the loser finds its element already handed back.
    /// </para>
    /// <para>
    /// The dance is the standard one: <c>GetIUnknownForObject</c> adds a
    /// reference on the object, <c>GetObjectForIUnknown</c> adds one on the
    /// wrapper, and the raw pointer is released to balance the first. The
    /// wrapper is left one higher than it started, which is exactly the
    /// reference the returned owner will hand back.
    /// </para>
    /// <para>
    /// The case that needs it: a text surface built on the focused element,
    /// cached across events, and still in use after focus has moved on and the
    /// provider has released its own reference.
    /// </para>
    /// </remarks>
    public ComOwned<T> OwnNewReference<T>(T value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Marshal.IsComObject(value))
        {
            // A test double or a managed stand-in: nothing to count.
            return Own(value);
        }

        var unknown = Marshal.GetIUnknownForObject(value);
        try
        {
            var second = (T)Marshal.GetObjectForIUnknown(unknown);
            return Own(second);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    internal void Enqueue(object value) => _pending.Enqueue(value);

    /// <summary>
    /// Release everything queued. Called from the dispatch loop between events.
    /// </summary>
    /// <returns>How many references were released.</returns>
    /// <remarks>
    /// Never throws. A provider that fails on release is a provider we cannot
    /// do anything about, and the alternative to swallowing it is tearing down
    /// the loop that keeps the reader speaking.
    /// </remarks>
    public int Drain()
    {
        var count = 0;
        while (_pending.TryDequeue(out var value))
        {
            try
            {
                _release(value);
            }
            catch (Exception ex) when (ex is InvalidComObjectException
                or COMException or ArgumentException or NotSupportedException)
            {
                // Already gone, or never a wrapper. Either way it is not
                // reachable from us any more, which was the point.
            }
            Interlocked.Decrement(ref _live);
            Interlocked.Increment(ref _released);
            count++;
        }
        return count;
    }

    private static int DefaultRelease(object value)
        => Marshal.IsComObject(value) ? Marshal.ReleaseComObject(value) : 0;
}
