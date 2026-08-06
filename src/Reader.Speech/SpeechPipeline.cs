using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Plugins;
using Aura.Abstractions.Output;
using Aura.Abstractions.Speech;
using Aura.Diagnostics;
using Aura.Output;
using Aura.Speech.Punctuation;
using Aura.Speech.Rendering;
using Aura.Speech.Queue;
using Aura.Speech.Rules;
using Serilog;

namespace Aura.Speech;

/// <summary>
/// Wires an <see cref="IAccessibilityProvider"/>'s event stream through the
/// <see cref="SpeechRuleEngine"/> into a <see cref="SpeechQueue"/>.
/// </summary>
/// <remarks>
/// The pipeline does not drive the engine. A separate consumer (typically the
/// host) drains the queue and calls <see cref="ISpeechEngine.SpeakAsync"/>.
/// This split lets tests assert on queue contents without requiring an engine.
/// </remarks>
public sealed class SpeechPipeline : IDisposable
{
    private readonly IAccessibilityProvider _provider;
    private volatile SpeechRuleEngine _engine;
    private readonly SpeechQueue _queue;
    private readonly ILogger _log;
    private readonly Func<ProcessInfo?>? _processInfo;
    private readonly TypingState? _typingState;
    private readonly OutputArbiter _arbiter = new();
    private readonly SpeechRenderer _renderer = new();
    private IDisposable? _subscription;
    private bool _started;
    private bool _disposed;

    public SpeechPipeline(
        IAccessibilityProvider provider,
        SpeechRuleEngine engine,
        SpeechQueue queue,
        Func<ProcessInfo?>? processInfo = null,
        TypingState? typingState = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _processInfo = processInfo;
        _typingState = typingState;
        _log = LoggerFactory.ForComponent("Speech.Pipeline");
    }

    /// <summary>
    /// Capital-letter announcement strategy. <c>"off"</c> | <c>"pitch"</c> |
    /// <c>"beep"</c> | <c>"both"</c>. Today only <c>"pitch"</c> is implemented;
    /// <c>"beep"</c> needs the audio mixer (Phase 4b) and silently degrades
    /// to <c>"off"</c> until then.
    /// </summary>
    public string CapitalLetterAnnouncement
    {
        get => _renderer.CapitalLetterAnnouncement;
        set => _renderer.CapitalLetterAnnouncement = value;
    }

    /// <summary>
    /// Atomically swap the rule engine. Used by the host when plugins
    /// register or remove <see cref="SpeechRule"/>s — the host rebuilds the
    /// engine from the static rule set plus the live plugin contributions
    /// and calls this method.
    /// </summary>
    public void UpdateRuleEngine(SpeechRuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
    }

    /// <summary>
    /// Decides which announcements survive when several producers describe the
    /// same user action. Every path into the queue goes through it.
    /// </summary>
    public OutputArbiter Arbiter => _arbiter;

    /// <summary>
    /// Punctuation level applied to every composed utterance before queueing.
    /// Cycled by <c>ReaderCommand.CyclePunctuationLevel</c>.
    /// </summary>
    public PunctuationLevel PunctuationLevel
    {
        get => _renderer.PunctuationLevel;
        set => _renderer.PunctuationLevel = value;
    }

    /// <summary>
    /// Decides whether a queued announcement is still worth speaking. Supplied
    /// by the host, which is the only thing that knows what the current focus
    /// is. Returning <c>null</c> means "unconditionally valid".
    /// </summary>
    public Func<SpeechRequest, IValidityPredicate?>? ValidityFor { get; set; }

    /// <summary>Renders presentations to utterances. Exposed for the transcript harness.</summary>
    public SpeechRenderer Renderer => _renderer;

    /// <summary>Subscribe to provider events. Idempotent.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;

        const AccessibilityEventKind kinds =
            AccessibilityEventKind.FocusChanged
            | AccessibilityEventKind.ValueChanged
            | AccessibilityEventKind.SelectionChanged
            | AccessibilityEventKind.SelectionAdded
            | AccessibilityEventKind.SelectionRemoved
            | AccessibilityEventKind.AlertRaised
            | AccessibilityEventKind.ToolTipOpened
            | AccessibilityEventKind.LiveRegionChanged
            | AccessibilityEventKind.CaretMoved;

        _subscription = _provider.Subscribe(kinds, OnEvent);
        _log.Debug("Speech pipeline subscribed to provider");
    }

    private void OnEvent(AccessibilityEvent ev)
    {
        try
        {
            // Suppress automatic value re-reads while the user is typing —
            // re-reading the growing value of an edit (Run dialog: "n", "no",
            // "not", "note") is exactly wrong, and char/word echo is the right
            // feedback for typing.
            //
            // Only for controls whose value the user is typing INTO. Space on a
            // checkbox counts as a typing keystroke, so this gate silenced every
            // checkbox toggle in the settings dialog: the toggle-state change
            // arrived a few milliseconds after the space and was dropped as if
            // it were an edit re-reading itself. A checkbox's value does not
            // grow as you type; it changes because you asked it to, and that is
            // precisely what has to be heard.
            //
            // We do NOT gate FocusChanged. Same-control focus re-fires are
            // already deduped in the provider; gating focus here would swallow
            // the first announcement of a control in a dialog opened by a
            // shortcut key (the Run box after Win+R), because that keystroke
            // sets the typing flag a moment before focus lands.
            if (_typingState?.IsTyping == true
                && ev.Kind == AccessibilityEventKind.ValueChanged
                && IsTextEntry(ev.Node?.Role))
            {
                return;
            }

            var reason = ev.Kind switch
            {
                AccessibilityEventKind.FocusChanged => SpeechReason.FocusChanged,
                AccessibilityEventKind.ValueChanged => SpeechReason.ValueChanged,
                AccessibilityEventKind.SelectionChanged => SpeechReason.SelectionChanged,
                AccessibilityEventKind.SelectionAdded => SpeechReason.SelectionAdded,
                AccessibilityEventKind.SelectionRemoved => SpeechReason.SelectionRemoved,
                AccessibilityEventKind.AlertRaised => SpeechReason.AlertRaised,
                AccessibilityEventKind.ToolTipOpened => SpeechReason.ToolTipOpened,
                AccessibilityEventKind.LiveRegionChanged => SpeechReason.LiveRegionUpdate,
                AccessibilityEventKind.CaretMoved => SpeechReason.CaretMoved,
                _ => SpeechReason.Unknown,
            };

            var request = NameSingleCharacter(new SpeechRequest(
                Reason: reason,
                Node: ev.Node,
                RawText: ev.CaretLine,
                AppExecutableName: _processInfo?.Invoke()?.ExecutableName,
                Extras: null));

            var presentation = _engine.Compose(request, ValidityFor?.Invoke(request));
            if (presentation is null)
            {
                _log.Verbose("Nothing to say for {Reason} on {Node}", reason, Redaction.Text(ev.Node?.Name));
                return;
            }

            if (_arbiter.Evaluate(presentation) == OutputDecision.Drop)
            {
                Trace(presentation, "dropped by arbiter");
                return;
            }

            var utterance = _renderer.Render(presentation);
            if (utterance.IsEmpty)
            {
                Trace(presentation, "rendered to nothing");
                return;
            }

            Trace(presentation, "queued");
            _queue.Enqueue(utterance);
        }
        catch (Exception ex) when (!IsCriticalException(ex))
        {
            _log.Error(ex, "speech pipeline failed handling {Kind}", ev.Kind);
        }
    }

    /// <summary>
    /// Push a synthetic request through the rule engine and into the queue.
    /// Used for read-character/word/line commands and user announcements.
    /// </summary>
    public bool Submit(SpeechRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request = NameSingleCharacter(request);
        var presentation = _engine.Compose(request, ValidityFor?.Invoke(request));
        if (presentation is null)
        {
            return false;
        }
        if (_arbiter.Evaluate(presentation) == OutputDecision.Drop)
        {
            return false;
        }
        var utterance = _renderer.Render(presentation);
        return !utterance.IsEmpty && _queue.Enqueue(utterance);
    }

    /// <summary>
    /// One line per announcement decision: what it was about, why it exists,
    /// which rules produced it, and what happened to it.
    /// </summary>
    /// <remarks>
    /// The subject is a node id and the rule trace is a list of rule names —
    /// neither is content, so both are logged in full even with redaction on.
    /// That is deliberate: when an announcement describes the wrong thing, the
    /// question is <em>which element did it describe</em>, and the id answers
    /// that without putting a word the user was reading on disk.
    /// </remarks>
    private void Trace(Presentation p, string outcome)
    {
        if (!_log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
        {
            return;
        }
        _log.Debug(
            "announce {Outcome}: reason={Reason} subject={Subject} priority={Priority} group={Group} rules=[{Rules}] text={Text}",
            outcome,
            p.Reason,
            p.Subject ?? "(none)",
            p.Priority,
            p.CancelGroup ?? "(none)",
            string.Join(" > ", p.RuleTrace),
            Redaction.Text(TranscriptRenderer.Instance.Render(p)));
    }

    /// <summary>
    /// Controls whose value the user builds by typing, and which therefore
    /// re-announce themselves noisily mid-word if left ungated.
    /// </summary>
    private static bool IsTextEntry(AccessibleRole? role) => role is
        AccessibleRole.Edit or AccessibleRole.PasswordEdit
        or AccessibleRole.ComboBox or AccessibleRole.Document;

    /// <summary>
    /// When the request's text is a single punctuation / symbol / whitespace
    /// character — i.e. the user navigated onto it character-by-character or
    /// invoked read-character — replace it with its spoken name ("," → "comma")
    /// so it is always heard. This must happen on the request <em>before</em>
    /// composing: the rule template renderer trims trailing separators, so a
    /// lone "," or " " would otherwise render to empty and produce no utterance
    /// at all. Letters and digits are left alone.
    /// </summary>
    private static SpeechRequest NameSingleCharacter(SpeechRequest request)
    {
        if (request.RawText is not { Length: 1 } raw)
        {
            return request;
        }
        var name = PunctuationFilter.SpokenName(raw[0]);
        return name is null ? request : request with { RawText = name };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _subscription?.Dispose();
        _subscription = null;
    }

    private static bool IsCriticalException(Exception ex)
        => ex is OutOfMemoryException or StackOverflowException or ThreadAbortException;
}
