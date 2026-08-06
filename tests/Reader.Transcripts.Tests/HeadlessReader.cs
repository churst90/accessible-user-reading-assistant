using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Output;
using Aura.Abstractions.Speech;
using Aura.Core.Output;
using Aura.Output;
using Aura.Speech.Queue;
using Aura.Speech.Rendering;
using Aura.Speech.Rules;
using Microsoft.Extensions.Time.Testing;

namespace Aura.Transcripts;

/// <summary>
/// The reader's announcement path, with no Windows, no audio and no clock.
/// </summary>
/// <remarks>
/// <para>
/// The real rule engine loaded from the real <c>defaults.yaml</c>, the real
/// renderer, the real arbiter, the real queue, and the real focus tracker. Only
/// three things are substituted: the accessibility provider (a script drives
/// events instead), the speech engine (the transcript renderer stands in), and
/// the clock.
/// </para>
/// <para>
/// Stubbing as little as possible is the point. Every announcement bug this
/// project has had lived in the arbiter's coincidence window, the queue's
/// coalescing, or the interaction between the two — so those must be the real
/// ones, and the clock must be fake so their timing is decided by the test
/// rather than by how busy the CI machine is.
/// </para>
/// <para>
/// <b>What this does not yet cover:</b> the host's own wiring in
/// <c>Program.cs</c> — key echo, caret following, the review cursor, the
/// watchdog. Those need the host's composition to be reachable without a
/// message loop, which it is not. Recorded as F5 open question 1.
/// </para>
/// </remarks>
public sealed class HeadlessReader : IDisposable
{
    private readonly SpeechRuleEngine _rules;
    private readonly SpeechRenderer _renderer = new();
    private readonly OutputArbiter _arbiter;
    private readonly SpeechQueue _queue;
    private readonly FocusTracker _focus = new();
    private readonly List<string> _spoken = [];

    public HeadlessReader()
    {
        Time = new FakeTimeProvider();
        _rules = new SpeechRuleEngine(YamlRuleLoader.LoadDefaults());
        _arbiter = new OutputArbiter(Time);
        _queue = new SpeechQueue(timeProvider: Time);
    }

    /// <summary>The clock the test drives.</summary>
    public FakeTimeProvider Time { get; }

    /// <summary>Everything that reached the engine, in order.</summary>
    public IReadOnlyList<string> Spoken => _spoken;

    /// <summary>Focus moves to <paramref name="node"/>.</summary>
    /// <remarks>
    /// Order copied from the host deliberately: record the new focus, then
    /// sweep, then announce. Sweeping first would evaluate every predicate
    /// against the previous focus and drop nothing.
    /// </remarks>
    public void Focus(AccessibleNode node)
    {
        _focus.OnFocusChanged(node);
        _queue.SweepInvalid();
        Submit(new SpeechRequest(SpeechReason.FocusChanged, node, null, null));
    }

    /// <summary>An event about a node that does not move focus.</summary>
    public void Event(SpeechReason reason, AccessibleNode? node, string? text = null)
        => Submit(new SpeechRequest(reason, node, text, null));

    /// <summary>Advance the clock. Nothing is spoken by the passage of time alone.</summary>
    public void Wait(TimeSpan by) => Time.Advance(by);

    /// <summary>
    /// Drain everything queued, as the host's drain loop would — including the
    /// speak-time validity check, which is where a stale announcement dies.
    /// </summary>
    public void Drain()
    {
        while (_queue.TryDequeue(out var utterance) && utterance is not null)
        {
            if (utterance.Validity is { } v && !v.IsStillValid())
            {
                continue;
            }
            _spoken.Add(utterance.PlainText());
        }
    }

    private void Submit(SpeechRequest request)
    {
        // Must mirror Program.cs's ValidityFor exactly. It does not today —
        // this is a copy — and that copy is why the harness agreed with a host
        // that was silently sweeping every selection announcement. Making the
        // host hand its policy to the harness is part of extracting a testable
        // host core (F5 open question 1); until then, changing one means
        // changing the other, and a transcript is the thing that notices.
        var validity = request.Reason is SpeechReason.FocusChanged
            ? _focus.For(request.Node?.Id.Value)
            : null;

        var presentation = _rules.Compose(request, validity);
        if (presentation is null)
        {
            return;
        }
        if (_arbiter.Evaluate(presentation) == OutputDecision.Drop)
        {
            return;
        }
        var utterance = _renderer.Render(presentation);
        if (!utterance.IsEmpty)
        {
            _queue.Enqueue(utterance);
        }
    }

    public void Dispose() => _queue.Dispose();
}
