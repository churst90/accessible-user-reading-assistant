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
/// The announcement policy — what makes an announcement stale, and the order
/// the reader must learn things in — is <see cref="AnnouncementPolicy"/>, the
/// same object the host uses. It used to be a copy here, and the copy is why
/// this suite agreed with a host that was sweeping every list announcement into
/// silence. Two implementations of one policy cannot disagree if there is only
/// one.
/// </para>
/// <para>
/// <b>Still not covered:</b> key echo, caret following, the review cursor and
/// the watchdog. Those remain inside the host's composition, reachable only
/// through a message loop. Each is a candidate to follow the policy out.
/// </para>
/// </remarks>
public sealed class HeadlessReader : IDisposable
{
    private readonly SpeechRuleEngine _rules;
    private readonly SpeechRenderer _renderer = new();
    private readonly OutputArbiter _arbiter;
    private readonly SpeechQueue _queue;
    private readonly AnnouncementPolicy _policy = new();
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
    public void Focus(AccessibleNode node)
    {
        _policy.OnFocusChanged(node, _queue.SweepInvalid);
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
        var presentation = _rules.Compose(request, _policy.ValidityFor(request));
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
