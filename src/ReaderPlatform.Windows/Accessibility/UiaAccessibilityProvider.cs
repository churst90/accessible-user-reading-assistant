using System.Globalization;
using System.Runtime.Versioning;
using System.Threading.Channels;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Aura.Abstractions.Accessibility;
using Aura.Diagnostics;
using Serilog;

namespace Aura.Platform.Windows.Accessibility;

/// <summary>
/// <see cref="IAccessibilityProvider"/> over the managed System.Windows.Automation
/// (UIA) API. Subscribes to focus events, marshals them off the UIA thread onto a
/// channel, and dispatches normalized <see cref="AccessibilityEvent"/>s to subscribers.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UiaAccessibilityProvider : IAccessibilityProvider
{
    private readonly Channel<RawUiaEvent> _events;

    private enum RawUiaEventKind { Focus, Value, Text, CaretMoved, Selection, Alert }
    private readonly record struct RawUiaEvent(
        RawUiaEventKind Kind,
        AutomationElement Element,
        string? CaretLine,
        string? CharBeforeCaret = null,
        string? SelectionText = null);
    private readonly object _gate = new();
    private readonly List<Subscription> _subscriptions = new();
    private readonly Dictionary<NodeId, AutomationElement> _elementCache = new();
    private readonly ILogger _log;
    private readonly CancellationTokenSource _cts = new();
    private AutomationFocusChangedEventHandler? _focusHandler;
    private AutomationPropertyChangedEventHandler? _valueChangedHandler;
    private AutomationEventHandler? _textChangedHandler;
    private AutomationEventHandler? _textSelectionHandler;
    private Task? _dispatchTask;
    private AccessibleNode? _focused;
    private AutomationElement? _focusedElement;
    private bool _started;
    private bool _disposed;

    public UiaAccessibilityProvider()
    {
        _events = Channel.CreateUnbounded<RawUiaEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _log = LoggerFactory.ForComponent("UIA");
    }

    public AccessibleNode? Focused
    {
        get { lock (_gate) { return _focused; } }
    }

    /// <summary>Look up the cached <see cref="AutomationElement"/> for a node id, if any.</summary>
    public AutomationElement? TryGetElement(NodeId id)
    {
        lock (_gate)
        {
            return _elementCache.TryGetValue(id, out var element) ? element : null;
        }
    }

    /// <summary>
    /// Read the current caret line, char-before-caret, and selection text from
    /// <paramref name="element"/>. Safe to call off the UIA event thread; will
    /// return defaults if the element no longer exposes <c>TextPattern</c>.
    /// </summary>
    public static (string? Line, string? CharBeforeCaret, string? SelectionText) ReadCaretSnapshot(AutomationElement element)
    {
        var snap = TryReadCaretSnapshot(element);
        return (snap.Line, snap.CharBeforeCaret, snap.SelectionText);
    }

    public AccessibleNode? Root => UiaNodeMapper.Map(AutomationElement.RootElement);

    /// <summary>
    /// Walk up the UIA tree from the focused element to the owning top-level
    /// Window and return its <c>Name</c>. Used by <c>Insert+T</c> ("read window
    /// title") so the user gets the application window title rather than the
    /// label of whatever control happens to be focused.
    /// </summary>
    public string? GetFocusedWindowTitle()
    {
        AutomationElement? element;
        lock (_gate)
        {
            element = _focusedElement;
        }
        return element is null ? null : GetTopLevelWindowName(element);
    }

    /// <summary>
    /// Walk up from <paramref name="element"/> to the first ancestor whose
    /// control type is <c>Window</c> or <c>Pane</c> (or the element itself if
    /// it already is one) and return its <c>NativeWindowHandle</c> + <c>Name</c>.
    /// Returns <c>(0, null)</c> when the chain is broken.
    /// </summary>
    public (nint Handle, string? Name) GetTopLevelWindowInfo(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var current = element;
            while (current is not null)
            {
                try
                {
                    var ct = current.Current.ControlType;
                    if (ct == ControlType.Window)
                    {
                        return ((nint)current.Current.NativeWindowHandle, current.Current.Name);
                    }
                }
                catch (ElementNotAvailableException)
                {
                    return (0, null);
                }
                current = walker.GetParent(current);
            }
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Warning(ex, "could not resolve top-level window from element");
        }
        return (0, null);
    }

    private string? GetTopLevelWindowName(AutomationElement element)
    {
        var (_, name) = GetTopLevelWindowInfo(element);
        return string.IsNullOrEmpty(name) ? null : name;
    }

    public AccessibleNode? FromPoint(int screenX, int screenY)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(screenX, screenY));
            return UiaNodeMapper.Map(element);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    public IDisposable Subscribe(AccessibilityEventKind kinds, Action<AccessibilityEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var sub = new Subscription(this, kinds, handler);
        lock (_gate)
        {
            _subscriptions.Add(sub);
        }
        return sub;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return ValueTask.CompletedTask;
        }
        _started = true;

        _focusHandler = OnFocusChanged;
        _valueChangedHandler = OnValueChanged;
        _textChangedHandler = OnTextChanged;
        _textSelectionHandler = OnTextSelectionChanged;
        try
        {
            // Registered under the cache request so delivered elements carry
            // their whole property set. Without the activation this still
            // works — it just silently costs ~28 cross-process round trips per
            // event instead of zero. See UiaCache.
            UiaCache.Activated(() =>
            {
                Automation.AddAutomationFocusChangedEventHandler(_focusHandler);
                RegisterDesktopWideEvents();
            });
            // Per-focus value / text / selection subscriptions are attached
            // lazily in OnFocusChanged so we don't pay tree-wide marshalling
            // costs. See AttachToElement.
            _log.Information("UIA focus event handler registered (cached)");
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Error(ex, "could not register UIA event handlers");
            throw;
        }

        _dispatchTask = Task.Run(() => DispatchLoopAsync(_cts.Token), _cts.Token);
        return ValueTask.CompletedTask;
    }

    private AutomationEventHandler? _desktopEventHandler;

    /// <summary>
    /// Subscribe to the events that fire on elements which are <em>not</em>
    /// focused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scoping every subscription to the focused element — correct and cheap
    /// for value and text changes — made these structurally unreachable. A
    /// toast, a dialog that opens without stealing focus, a tooltip, a list
    /// selection driven by the mouse: by definition none of them are the
    /// focused element, so no per-focus subscription could ever deliver them.
    /// Registering the handlers was not enough; the scope was the bug.
    /// </para>
    /// <para>
    /// <b>Not available through this API:</b> <c>UIA_LiveRegionChangedEventId</c>
    /// (20024) and <c>UIA_NotificationEventId</c> (20023) have no
    /// <c>System.Windows.Automation</c> equivalent. Notification is how modern
    /// Windows applications announce transient status, so web live regions and
    /// most app toasts stay silent until the native COM migration. The rule
    /// <c>core.liveregion.changed</c> is already in place for when they arrive.
    /// </para>
    /// <para>
    /// Subtree scope over the desktop is deliberately broad and is the main
    /// cost centre here. It wants measuring on a busy machine; native UIA's
    /// <c>CoalesceEvents</c> is the mitigation if it proves too chatty.
    /// </para>
    /// </remarks>
    private void RegisterDesktopWideEvents()
    {
        _desktopEventHandler = OnDesktopEvent;
        var root = AutomationElement.RootElement;

        Subscribe(WindowPattern.WindowOpenedEvent, "window-opened");
        Subscribe(SelectionItemPattern.ElementSelectedEvent, "element-selected");
        Subscribe(AutomationElement.MenuOpenedEvent, "menu-opened");
        Subscribe(AutomationElement.ToolTipOpenedEvent, "tooltip-opened");

        void Subscribe(AutomationEvent automationEvent, string label)
        {
            try
            {
                Automation.AddAutomationEventHandler(
                    automationEvent, root, TreeScope.Subtree, _desktopEventHandler);
                _log.Debug("registered desktop-wide {Event}", label);
            }
            catch (Exception ex) when (!IsCritical(ex))
            {
                // One unavailable event must not cost us the others.
                _log.Warning(ex, "could not register desktop-wide {Event}", label);
            }
        }
    }

    private void OnDesktopEvent(object? sender, AutomationEventArgs e)
    {
        try
        {
            if (sender is not AutomationElement element)
            {
                return;
            }
            var kind = e.EventId == SelectionItemPattern.ElementSelectedEvent
                ? RawUiaEventKind.Selection
                : RawUiaEventKind.Alert;
            _events.Writer.TryWrite(new RawUiaEvent(kind, element, CaretLine: null));
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Warning(ex, "ignored exception in desktop-wide UIA handler");
        }
    }

    /// <summary>
    /// Dispatch an event that arrived from an element other than the focused
    /// one. Unlike the focus path, there is no dedup: a toast that appears
    /// twice really did appear twice.
    /// </summary>
    private void HandleUnfocusedEvent(AutomationElement element, AccessibilityEventKind kind)
    {
        var node = UiaNodeMapper.MapCached(element);
        if (node is null)
        {
            return;
        }
        // An element with nothing to say is not worth interrupting for.
        if (string.IsNullOrEmpty(node.Name) && string.IsNullOrEmpty(node.Value))
        {
            return;
        }
        DispatchLocal(new AccessibilityEvent(kind, node, DateTimeOffset.UtcNow));
    }

    // Per-focus subscriptions. Reattached on every focus change so we never
    // pay tree-wide marshalling costs and we don't have to compare the sender
    // element to the focused element on every event.
    private AutomationElement? _subscribedElement;

    private void OnFocusChanged(object? sender, AutomationFocusChangedEventArgs e)
    {
        // UIA hooks have a ~300 ms cliff. We do the bare minimum here:
        // capture the element, queue a raw-element marker, and let the
        // dispatch loop do the slower mapping/event work.
        try
        {
            if (sender is not AutomationElement element)
            {
                return;
            }
            _events.Writer.TryWrite(new RawUiaEvent(RawUiaEventKind.Focus, element, CaretLine: null));
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Warning(ex, "ignored exception in UIA focus handler");
        }
    }

    private void OnValueChanged(object? sender, AutomationPropertyChangedEventArgs e)
    {
        try
        {
            if (sender is not AutomationElement element)
            {
                return;
            }
            _events.Writer.TryWrite(new RawUiaEvent(RawUiaEventKind.Value, element, CaretLine: null));
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Warning(ex, "ignored exception in UIA value-changed handler");
        }
    }

    private void OnTextChanged(object? sender, AutomationEventArgs e)
    {
        try
        {
            if (sender is not AutomationElement element)
            {
                return;
            }
            _events.Writer.TryWrite(new RawUiaEvent(RawUiaEventKind.Text, element, CaretLine: null));
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Warning(ex, "ignored exception in UIA text-changed handler");
        }
    }

    private void OnTextSelectionChanged(object? sender, AutomationEventArgs e)
    {
        // The caret moved (or the selection changed) inside a text control.
        // We used to read the caret line, the character before it, and the
        // selection text right here, inside the UIA callback. That was three
        // cross-process TextPattern reads on the callback thread, which has a
        // ~300 ms budget before UIA starts dropping events.
        //
        // Nothing needs them now: CaretTracker samples the position itself and
        // works out what changed. This handler's only job is to say "look
        // again", so it queues a bare marker and returns.
        try
        {
            if (sender is not AutomationElement element)
            {
                return;
            }
            _events.Writer.TryWrite(new RawUiaEvent(RawUiaEventKind.CaretMoved, element, CaretLine: null));
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Warning(ex, "ignored exception in UIA text-selection handler");
        }
    }

    private readonly record struct CaretSnapshot(string? Line, string? CharBeforeCaret, string? SelectionText);

    private static CaretSnapshot TryReadCaretSnapshot(AutomationElement element)
    {
        try
        {
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var raw)
                || raw is not TextPattern textPattern)
            {
                return default;
            }
            var selection = textPattern.GetSelection();
            if (selection is null || selection.Length == 0)
            {
                return default;
            }
            var caretRange = selection[0];

            // Selection text — non-empty when shift-selecting; null when just
            // the caret is positioned (collapsed range).
            string? selectionText = null;
            try
            {
                var selText = caretRange.GetText(8192);
                if (!string.IsNullOrEmpty(selText))
                {
                    selectionText = selText.TrimEnd('\r', '\n');
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
            {
            }

            // Line under the caret (NVDA-style line read).
            string? line = null;
            try
            {
                var lineRange = caretRange.Clone();
                lineRange.ExpandToEnclosingUnit(TextUnit.Line);
                var lineText = lineRange.GetText(2048);
                line = string.IsNullOrEmpty(lineText) ? null : lineText.TrimEnd('\r', '\n');
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
            {
            }

            // Single character to the LEFT of the caret start. We move the
            // start endpoint one character backward and read the resulting
            // 1-char range. Used by Backspace echo: the keyboard event arrives
            // on a separate channel after the OS has already deleted, so we
            // cache this here (pre-deletion side) and announce from cache.
            string? charBefore = null;
            try
            {
                var beforeRange = caretRange.Clone();
                var moved = beforeRange.MoveEndpointByUnit(
                    TextPatternRangeEndpoint.Start, TextUnit.Character, -1);
                if (moved != 0)
                {
                    // Collapse end back to start+1 character so we get exactly
                    // one char, not the whole range up to the caret.
                    beforeRange.MoveEndpointByRange(
                        TextPatternRangeEndpoint.End, caretRange, TextPatternRangeEndpoint.Start);
                    var ch = beforeRange.GetText(4);
                    charBefore = string.IsNullOrEmpty(ch) ? null : ch;
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
            {
            }

            return new CaretSnapshot(line, charBefore, selectionText);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
        {
            return default;
        }
    }

    /// <summary>
    /// Identity of a focus target for de-duplication purposes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Runtime id alone is not enough: controls like the Run dialog's editable
    /// combo re-fire <c>FocusChanged</c> on every arrow-key value scroll, and
    /// UIA hands out a fresh runtime id each time for what is logically the
    /// same control.
    /// </para>
    /// <para>
    /// The previous approach — matching on <c>(Role, Name)</c> inside a
    /// sliding 750 ms window — suppressed real focus changes. <c>Name</c> is
    /// nullable and compared ordinally, so <em>every unnamed control of the
    /// same role matched every other one</em>. A toolbar of icon buttons with
    /// no accessible name went silent after the first button for as long as
    /// the user kept arrowing, which is precisely the situation where
    /// positional announcement matters most. So did a grid column of repeated
    /// values.
    /// </para>
    /// <para>
    /// Including the bounding rectangle fixes it without a timer at all. Two
    /// distinct controls essentially never occupy the same screen rectangle,
    /// while one control re-firing always does. No time window means no
    /// dependence on machine load, and a genuine return to a control after
    /// visiting another one still announces, because the intervening control
    /// displaced the stored key.
    /// </para>
    /// </remarks>
    private static string FocusKey(AccessibleNode node)
    {
        // Extras is never null (AccessibleNode substitutes an empty map).
        var bounds = Extra(node, "uia.Bounds");
        var automationId = Extra(node, "uia.AutomationId");
        return string.Concat(
            ((int)node.Role).ToString(CultureInfo.InvariantCulture), "",
            node.Name ?? string.Empty, "",
            automationId ?? string.Empty, "",
            // Falling back to the runtime id when the provider reports no
            // rectangle degrades to "never dedup", which is the safe
            // direction: a duplicate announcement is an annoyance, a missing
            // one is the user not knowing where they are.
            bounds ?? node.Id.Value);

        static string? Extra(AccessibleNode n, string key)
            => n.Extras.TryGetValue(key, out var raw) ? raw as string : null;
    }

    private string? _lastFocusKey;

    private void HandleFocusChanged(AutomationElement element)
    {
        // Cached: the element arrived from a subscription registered under
        // UiaCache, so the whole mapping below costs no round trips.
        var node = UiaNodeMapper.MapCached(element);
        if (node is null)
        {
            return;
        }

        var key = FocusKey(node);
        AutomationElement? toUnsubscribe = null;
        bool isSameNode;
        bool isSameControl;
        lock (_gate)
        {
            isSameNode = _focused is { } prev && prev.Id == node.Id;
            isSameControl = string.Equals(_lastFocusKey, key, StringComparison.Ordinal);

            _focused = node;
            _focusedElement = element;
            if (!isSameNode)
            {
                _elementCache.Clear();
                toUnsubscribe = _subscribedElement;
                _subscribedElement = null;
            }
            _elementCache[node.Id] = element;
            _lastFocusKey = key;
        }

        if (isSameNode)
        {
            // The very same element re-firing focus, common while typing in
            // legacy edits and combos. Nothing changed; nothing to say.
            return;
        }

        // Resubscribe pattern handlers to the new element even when the
        // announcement is suppressed — value/text events still need routing.
        DetachFromElement(toUnsubscribe);
        AttachToElement(element);

        if (isSameControl)
        {
            // Same control, fresh runtime id (the Run box readback). Already
            // announced; re-announcing is the bug this guards against.
            return;
        }

        // For editable / document text controls, capture the line at the caret
        // so the focus announcement reads the CURRENT LINE (via the {text}
        // token), not the whole value. A single-line edit's caret line is its
        // full value, so search boxes / address bars / the Run box still read
        // their content; a multi-line edit reads only the current line instead
        // of dumping the buffer. This runs on the dispatch loop (off the UIA
        // callback thread), so the TextPattern read is safe and the read is
        // already wrapped against teardown exceptions.
        string? caretLine = null;
        if (ShouldReadCaretLineOnFocus(node))
        {
            caretLine = TryReadCaretSnapshot(element).Line;
        }

        DispatchLocal(new AccessibilityEvent(AccessibilityEventKind.FocusChanged, node, DateTimeOffset.UtcNow, caretLine));
    }

    /// <summary>
    /// True for focus targets whose current caret line should be announced
    /// (edits and documents). Password fields are excluded — UIA reports them
    /// as <see cref="AccessibleRole.Edit"/> with <see cref="AccessibleStates.Protected"/>
    /// set (there is no distinct password control type), and we must never read
    /// their content aloud.
    /// </summary>
    private static bool ShouldReadCaretLineOnFocus(AccessibleNode node)
    {
        if ((node.States & AccessibleStates.Protected) != 0)
        {
            return false;
        }
        return node.Role is AccessibleRole.Edit or AccessibleRole.Document;
    }

    private void HandleValueChanged(AutomationElement element)
    {
        if (!IsFocused(element))
        {
            return;
        }
        var node = UiaNodeMapper.MapCached(element);
        if (node is null)
        {
            return;
        }
        DispatchLocal(new AccessibilityEvent(AccessibilityEventKind.ValueChanged, node, DateTimeOffset.UtcNow));
    }

    private void HandleCaretMoved(AutomationElement element, string? caretLine, string? charBeforeCaret, string? selectionText)
    {
        if (!IsFocused(element))
        {
            return;
        }
        var node = UiaNodeMapper.MapCached(element);
        if (node is null)
        {
            return;
        }
        DispatchLocal(new AccessibilityEvent(
            AccessibilityEventKind.CaretMoved,
            node,
            DateTimeOffset.UtcNow,
            caretLine,
            charBeforeCaret,
            selectionText));
    }

    private bool IsFocused(AutomationElement candidate)
    {
        AutomationElement? focused;
        lock (_gate)
        {
            focused = _focusedElement;
        }
        if (focused is null)
        {
            return false;
        }
        try
        {
            return Automation.Compare(candidate, focused);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException)
        {
            return false;
        }
    }

    /// <summary>
    /// Attach value / text-changed / text-selection-changed handlers scoped to
    /// a single element. Cheaper than RootElement/Subtree subscriptions and
    /// removes the need to filter by sender on every event.
    /// </summary>
    private void AttachToElement(AutomationElement element)
    {
        if (_valueChangedHandler is null || _textChangedHandler is null || _textSelectionHandler is null)
        {
            return;
        }
        try
        {
            UiaCache.Activated(() =>
            {
                Automation.AddAutomationPropertyChangedEventHandler(
                    element,
                    TreeScope.Element,
                    _valueChangedHandler,
                    ValuePattern.ValueProperty,
                    RangeValuePattern.ValueProperty);

                Automation.AddAutomationEventHandler(
                    TextPattern.TextChangedEvent,
                    element,
                    TreeScope.Element,
                    _textChangedHandler);

                Automation.AddAutomationEventHandler(
                    TextPattern.TextSelectionChangedEvent,
                    element,
                    TreeScope.Element,
                    _textSelectionHandler);
            });

            lock (_gate)
            {
                _subscribedElement = element;
            }
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Verbose(ex, "could not attach per-focus event handlers (element may not support them)");
        }
    }

    private void DetachFromElement(AutomationElement? element)
    {
        if (element is null
            || _valueChangedHandler is null
            || _textChangedHandler is null
            || _textSelectionHandler is null)
        {
            return;
        }
        try { Automation.RemoveAutomationPropertyChangedEventHandler(element, _valueChangedHandler); }
        catch (Exception ex) when (!IsCritical(ex)) { _log.Verbose(ex, "ignored detach value-changed"); }
        try { Automation.RemoveAutomationEventHandler(TextPattern.TextChangedEvent, element, _textChangedHandler); }
        catch (Exception ex) when (!IsCritical(ex)) { _log.Verbose(ex, "ignored detach text-changed"); }
        try { Automation.RemoveAutomationEventHandler(TextPattern.TextSelectionChangedEvent, element, _textSelectionHandler); }
        catch (Exception ex) when (!IsCritical(ex)) { _log.Verbose(ex, "ignored detach text-selection"); }
    }

    private void DispatchLocal(AccessibilityEvent ev)
    {
        Subscription[] snapshot;
        lock (_gate)
        {
            snapshot = _subscriptions.ToArray();
        }
        foreach (var sub in snapshot)
        {
            if ((sub.Kinds & ev.Kind) == 0)
            {
                continue;
            }
            try
            {
                sub.Handler(ev);
            }
            catch (Exception ex) when (!IsCritical(ex))
            {
                _log.Warning(ex, "subscriber threw on {Kind}", ev.Kind);
            }
        }
    }

    private async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var raw in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    switch (raw.Kind)
                    {
                        case RawUiaEventKind.Focus:
                            HandleFocusChanged(raw.Element);
                            break;
                        case RawUiaEventKind.Value:
                        case RawUiaEventKind.Text:
                            HandleValueChanged(raw.Element);
                            break;
                        case RawUiaEventKind.CaretMoved:
                            HandleCaretMoved(raw.Element, raw.CaretLine, raw.CharBeforeCaret, raw.SelectionText);
                            break;
                        case RawUiaEventKind.Selection:
                            HandleUnfocusedEvent(raw.Element, AccessibilityEventKind.SelectionChanged);
                            break;
                        case RawUiaEventKind.Alert:
                            HandleUnfocusedEvent(raw.Element, AccessibilityEventKind.AlertRaised);
                            break;
                    }
                }
                catch (Exception ex) when (!IsCritical(ex))
                {
                    _log.Warning(ex, "dispatch loop threw on {Kind}", raw.Kind);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_focusHandler is not null)
        {
            try
            {
                Automation.RemoveAutomationFocusChangedEventHandler(_focusHandler);
            }
            catch (Exception ex) when (!IsCritical(ex))
            {
                _log.Warning(ex, "ignored exception while removing focus handler");
            }
            _focusHandler = null;
        }

        AutomationElement? subscribed;
        lock (_gate)
        {
            subscribed = _subscribedElement;
            _subscribedElement = null;
        }
        DetachFromElement(subscribed);

        try
        {
            Automation.RemoveAllEventHandlers();
        }
        catch (Exception ex) when (!IsCritical(ex))
        {
            _log.Warning(ex, "ignored exception while removing UIA event handlers");
        }
        _valueChangedHandler = null;
        _textChangedHandler = null;
        _textSelectionHandler = null;

        _events.Writer.TryComplete();
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_dispatchTask is not null)
        {
            try
            {
                await _dispatchTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }
        _cts.Dispose();

        lock (_gate)
        {
            _subscriptions.Clear();
        }
    }

    private static bool IsCritical(Exception ex)
        => ex is OutOfMemoryException or StackOverflowException or ThreadAbortException;

    private sealed class Subscription : IDisposable
    {
        private readonly UiaAccessibilityProvider _owner;
        public AccessibilityEventKind Kinds { get; }
        public Action<AccessibilityEvent> Handler { get; }

        public Subscription(UiaAccessibilityProvider owner, AccessibilityEventKind kinds, Action<AccessibilityEvent> handler)
        {
            _owner = owner;
            Kinds = kinds;
            Handler = handler;
        }

        public void Dispose()
        {
            lock (_owner._gate)
            {
                _owner._subscriptions.Remove(this);
            }
        }
    }
}
