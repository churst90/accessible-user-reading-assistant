using Aura.Abstractions.Speech;

namespace Aura.Core.Review;

/// <summary>
/// Drives a "say-all" read: pumps successive lines from a
/// <see cref="ReviewCursor"/> into a host-supplied submit callback at
/// <see cref="SpeechPriority.Background"/> until the cursor runs out or the
/// run is cancelled.
/// </summary>
/// <remarks>
/// <para>
/// Each line is emitted as a <see cref="SpeechReason.ReadAll"/> request. The
/// caller's submit function is responsible for routing it through the speech
/// pipeline; the runner doesn't know about engines or queues.
/// </para>
/// <para>
/// Pacing is intentionally simple: we wait <see cref="LinePauseMs"/> between
/// submissions. The queue and engine handle ordering and cancellation; if the
/// user presses Stop, <see cref="Cancel"/> stops the pump.
/// </para>
/// </remarks>
public sealed class SayAllRunner
{
    private readonly ReviewCursor _cursor;
    private readonly Func<SpeechRequest, bool> _submit;
    private CancellationTokenSource? _cts;

    public SayAllRunner(ReviewCursor cursor, Func<SpeechRequest, bool> submit)
    {
        _cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
        _submit = submit ?? throw new ArgumentNullException(nameof(submit));
    }

    /// <summary>Pause between successive line submissions, in milliseconds.</summary>
    public int LinePauseMs { get; set; } = 80;

    public bool IsRunning => _cts is not null;

    /// <summary>
    /// Start a say-all read from the current review-cursor position. Cancels
    /// any currently-running read first.
    /// </summary>
    public Task StartAsync()
    {
        return StartInternalAsync(rewindToStart: false);
    }

    /// <summary>
    /// Start a say-all read from the beginning of the focused content,
    /// rewinding the review cursor first. This is the user-facing
    /// "say all" command: read the document from the top regardless of where
    /// review currently sits.
    /// </summary>
    public Task StartFromBeginningAsync()
    {
        return StartInternalAsync(rewindToStart: true);
    }

    private Task StartInternalAsync(bool rewindToStart)
    {
        Cancel();
        if (rewindToStart)
        {
            _cursor.MoveToStart();
        }
        var cts = new CancellationTokenSource();
        _cts = cts;
        return Task.Run(() => RunAsync(cts.Token), cts.Token);
    }

    /// <summary>Stop the active read, if any.</summary>
    public void Cancel()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Speak the line under the cursor first, then walk forward.
            var line = _cursor.ReadCurrentLine();
            if (!string.IsNullOrEmpty(line))
            {
                Submit(line);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var next = _cursor.MoveNextLine();
                if (string.IsNullOrEmpty(next))
                {
                    return;
                }
                Submit(next);
                await Task.Delay(LinePauseMs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        finally
        {
            Interlocked.CompareExchange(ref _cts, null, _cts);
        }
    }

    private void Submit(string text)
    {
        _submit(new SpeechRequest(SpeechReason.ReadAll, Node: null, RawText: text, AppExecutableName: null));
    }
}
