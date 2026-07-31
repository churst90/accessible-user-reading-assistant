using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Plugins;
using OpenReader.Abstractions.Speech;
using OpenReader.Diagnostics;
using OpenReader.Speech.Punctuation;
using OpenReader.Speech.Queue;
using OpenReader.Speech.Rules;
using Serilog;

namespace OpenReader.Speech;

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
    public string CapitalLetterAnnouncement { get; set; } = "pitch";

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
    /// Punctuation level applied to every composed utterance before queueing.
    /// Cycled by <c>ReaderCommand.CyclePunctuationLevel</c>.
    /// </summary>
    public PunctuationLevel PunctuationLevel { get; set; } = PunctuationLevel.Some;

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
            | AccessibilityEventKind.AlertRaised
            | AccessibilityEventKind.LiveRegionChanged
            | AccessibilityEventKind.CaretMoved;

        _subscription = _provider.Subscribe(kinds, OnEvent);
        _log.Debug("Speech pipeline subscribed to provider");
    }

    private void OnEvent(AccessibilityEvent ev)
    {
        try
        {
            // Suppress automatic value/text re-reads during active typing — the
            // user gets char/word echo for typing feedback, and re-reading the
            // growing value (Run dialog "n", "no", "not", "note"...) is exactly
            // the wrong behavior. The typing flag is set synchronously in the
            // keyboard hook (see Win32KeyboardHook.SetTextInputObserver), so it
            // is reliably true before the UIA value-changed event arrives.
            //
            // We do NOT gate FocusChanged here. Same-control focus re-fires
            // (the other way legacy edits/combos spam events while typing) are
            // already deduped in UiaAccessibilityProvider; gating focus here as
            // well would swallow the first announcement of a control in a dialog
            // opened by a shortcut key (e.g. the Run box after Win+R), because
            // that keystroke sets the typing flag a moment before focus lands.
            if (_typingState?.IsTyping == true && ev.Kind == AccessibilityEventKind.ValueChanged)
            {
                return;
            }

            var reason = ev.Kind switch
            {
                AccessibilityEventKind.FocusChanged => SpeechReason.FocusChanged,
                AccessibilityEventKind.ValueChanged => SpeechReason.ValueChanged,
                AccessibilityEventKind.SelectionChanged => SpeechReason.SelectionChanged,
                AccessibilityEventKind.AlertRaised => SpeechReason.AlertRaised,
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

            var utterance = _engine.Compose(request);
            if (utterance is null)
            {
                _log.Verbose("No utterance for {Reason} on {Node}", reason, Redaction.Text(ev.Node?.Name));
                return;
            }

            utterance = ApplyPunctuation(utterance);
            utterance = ApplyCapitalCue(utterance);

            var enqueued = _queue.Enqueue(utterance);
            if (!enqueued)
            {
                _log.Verbose("Coalesced utterance: {Text}", Redaction.Text(utterance.Text));
            }
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
        var utterance = _engine.Compose(request);
        if (utterance is null)
        {
            return false;
        }
        utterance = ApplyPunctuation(utterance);
        utterance = ApplyCapitalCue(utterance);
        return _queue.Enqueue(utterance);
    }

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

    private SpeechUtterance ApplyPunctuation(SpeechUtterance utterance)
    {
        var filtered = PunctuationFilter.Apply(utterance.Text, PunctuationLevel);
        return ReferenceEquals(filtered, utterance.Text) ? utterance : utterance with { Text = filtered };
    }

    /// <summary>
    /// When the speech pipeline is announcing a single uppercase character
    /// (read-character, char echo on backspace etc.), bump the pitch so the
    /// user hears the difference between "A" and "a". Strategy is configurable;
    /// the audio-cue ("beep") variant needs the audio mixer (Phase 4b) and
    /// degrades to no-op for now.
    /// </summary>
    private SpeechUtterance ApplyCapitalCue(SpeechUtterance utterance)
    {
        var mode = CapitalLetterAnnouncement;
        if (string.IsNullOrEmpty(mode) || string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase))
        {
            return utterance;
        }
        if (utterance.Text.Length != 1)
        {
            return utterance;
        }
        if (!char.IsUpper(utterance.Text[0]))
        {
            return utterance;
        }
        // "pitch" or "both" — boost pitch by ~6 semitones so it's clearly
        // distinguishable. "beep" alone is a no-op until audio themes ship.
        if (string.Equals(mode, "pitch", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "both", StringComparison.OrdinalIgnoreCase))
        {
            var bumped = utterance.Prosody with { PitchDelta = utterance.Prosody.PitchDelta + 6f };
            return utterance with { Prosody = bumped };
        }
        return utterance;
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
