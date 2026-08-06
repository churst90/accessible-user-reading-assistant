using System.Globalization;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Aura.Abstractions.Accessibility;
using Aura.Diagnostics;
using Serilog;
using Windows.Win32.UI.Accessibility;

namespace Aura.Platform.Windows.Accessibility.Native;

/// <summary>
/// <see cref="IAccessibilityProvider"/> over the native UI Automation client.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the <c>System.Windows.Automation</c> implementation. What that one
/// could not do, and this one can:
/// </para>
/// <list type="bullet">
///   <item><b>Live regions and notifications.</b> ARIA live regions and modern
///   app toasts were entirely silent — the managed client has no event for
///   either.</item>
///   <item><b>Event coalescing.</b> A busy page can raise thousands of events
///   a second; without coalescing the dispatch loop drowns.</item>
///   <item><b>Heading level and link target</b> as text attributes, which is
///   what Read-mode quick navigation is built on.</item>
/// </list>
/// <para>
/// Structure deliberately mirrors the class it replaces: handlers do the bare
/// minimum and queue to a channel, and all mapping happens on the dispatch
/// loop. UIA callbacks run on a provider thread with a limited budget, and
/// doing real work inside one causes UIA to start dropping events.
/// </para>
/// </remarks>
// windows6.1 rather than bare "windows": the native UIA COM surface is
// annotated 6.1+, and an unversioned claim asserts support back to XP.
[SupportedOSPlatform("windows6.1")]
public sealed class NativeUiaProvider : IAccessibilityProvider
{
    private enum RawKind { Focus, Value, Text, CaretMoved, Selection, Alert, ToolTip, LiveRegion, Notification }

    private readonly record struct RawEvent(
        RawKind Kind,
        IUIAutomationElement Element,
        string? Text = null);

    private readonly Channel<RawEvent> _events;
    private readonly object _gate = new();
    private readonly List<Subscription> _subscriptions = new();
    private readonly Dictionary<NodeId, IUIAutomationElement> _elementCache = new();
    private readonly ILogger _log;
    private readonly CancellationTokenSource _cts = new();

    private IUIAutomation? _automation;
    private IUIAutomationCacheRequest? _cacheRequest;
    private FocusSink? _focusSink;
    private EventSink? _eventSink;
    private NotificationSink? _notificationSink;
    private PropertySink? _propertySink;
    private Task? _dispatchTask;
    private AccessibleNode? _focused;
    private IUIAutomationElement? _focusedElement;
    private string? _lastFocusKey;
    private bool _started;
    private bool _disposed;

    public NativeUiaProvider()
    {
        _events = Channel.CreateUnbounded<RawEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _log = LoggerFactory.ForComponent("UIA.Native");
    }

    public AccessibleNode? Focused
    {
        get { lock (_gate) { return _focused; } }
    }

    public AccessibleNode? Root
    {
        get
        {
            try
            {
                return _automation is null ? null : NativeUiaNodeMapper.Map(_automation.GetRootElement());
            }
            catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
            {
                return null;
            }
        }
    }

    /// <summary>The live element behind a node id, if it is the one in focus.</summary>
    internal IUIAutomationElement? TryGetElement(NodeId id)
    {
        lock (_gate)
        {
            return _elementCache.TryGetValue(id, out var element) ? element : null;
        }
    }

    internal IUIAutomation? Automation => _automation;

    /// <summary>
    /// The title of the window owning the focused element. Used by "report
    /// title", which must give the application window rather than the label of
    /// whatever control happens to be focused.
    /// </summary>
    public string? GetFocusedWindowTitle()
    {
        IUIAutomationElement? element;
        lock (_gate)
        {
            element = _focusedElement;
        }
        var (_, name) = GetTopLevelWindowInfo(element);
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Walk up to the owning top-level window and return its handle and name.
    /// Returns <c>(0, null)</c> when the chain is broken.
    /// </summary>
    internal (nint Handle, string? Name) GetTopLevelWindowInfo(IUIAutomationElement? element)
    {
        if (element is null || _automation is null)
        {
            return (0, null);
        }
        try
        {
            var walker = _automation.ControlViewWalker;
            var current = element;
            // Bounded: a pathological or cyclic tree must not spin here on the
            // dispatch loop.
            for (var depth = 0; current is not null && depth < 64; depth++)
            {
                var controlType = current.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_ControlTypePropertyId);
                if (controlType is int ct && ct == (int)UIA_CONTROLTYPE_ID.UIA_WindowControlTypeId)
                {
                    var handle = current.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_NativeWindowHandlePropertyId);
                    var name = current.GetCurrentPropertyValue(UIA_PROPERTY_ID.UIA_NamePropertyId);
                    return (handle is int h ? h : 0, name as string);
                }
                current = walker.GetParentElement(current);
            }
        }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
        {
            _log.Warning(ex, "could not resolve the top-level window");
        }
        return (0, null);
    }

    /// <summary>The window owning the currently focused element.</summary>
    public (nint Handle, string? Name) GetFocusedWindowInfo()
    {
        IUIAutomationElement? element;
        lock (_gate)
        {
            element = _focusedElement;
        }
        return GetTopLevelWindowInfo(element);
    }

    public AccessibleNode? FromPoint(int screenX, int screenY)
    {
        try
        {
            return _automation is null
                ? null
                : NativeUiaNodeMapper.Map(_automation.ElementFromPoint(new System.Drawing.Point(screenX, screenY)));
        }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
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

        _automation = NativeUia.Create();
        // IUIAutomation6 arrived in Windows 10 1809. Everything below degrades
        // gracefully without it — no coalescing, no notification events.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            NativeUia.TryEnableCoalescing(_automation);
        }
        _cacheRequest = NativeUia.BuildCacheRequest(_automation);

        _focusSink = new FocusSink(this);
        _eventSink = new EventSink(this);

        try
        {
            _automation.AddFocusChangedEventHandler(_cacheRequest, _focusSink);
            _log.Information("native UIA focus handler registered (cached, coalescing requested)");
        }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
        {
            _log.Error(ex, "could not register the native UIA focus handler");
            throw;
        }

        RegisterDesktopEvents();
        RegisterPropertyChanges();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            RegisterNotifications();
        }

        _dispatchTask = Task.Run(() => DispatchLoopAsync(_cts.Token), _cts.Token);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Events that fire on elements which are not focused, so they can never
    /// arrive through a per-focus subscription. Scoping every handler to the
    /// focused element is what previously made alerts unreachable.
    /// </summary>
    private void RegisterDesktopEvents()
    {
        if (_automation is null || _eventSink is null || _cacheRequest is null)
        {
            return;
        }
        var root = _automation.GetRootElement();

        Register(UIA_EVENT_ID.UIA_LiveRegionChangedEventId, "live-region");
        Register(UIA_EVENT_ID.UIA_Window_WindowOpenedEventId, "window-opened");
        Register(UIA_EVENT_ID.UIA_SelectionItem_ElementSelectedEventId, "element-selected");
        Register(UIA_EVENT_ID.UIA_MenuOpenedEventId, "menu-opened");
        Register(UIA_EVENT_ID.UIA_ToolTipOpenedEventId, "tooltip-opened");
        Register(UIA_EVENT_ID.UIA_Text_TextSelectionChangedEventId, "text-selection");
        Register(UIA_EVENT_ID.UIA_Text_TextChangedEventId, "text-changed");

        void Register(UIA_EVENT_ID id, string label)
        {
            try
            {
                _automation.AddAutomationEventHandler(
                    id, root, TreeScope.TreeScope_Subtree, _cacheRequest, _eventSink);
                _log.Debug("registered desktop-wide {Event}", label);
            }
            catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
            {
                // One unavailable event must not cost us the others.
                _log.Warning(ex, "could not register {Event}", label);
            }
        }
    }

    /// <summary>
    /// Property-changed subscriptions, which are a <em>separate</em>
    /// registration from the event handlers above.
    /// </summary>
    /// <remarks>
    /// Without these, checking a checkbox, expanding a tree node, or changing
    /// a combo box announced nothing at all: the control raises a property
    /// change, not an automation event, and we were only listening for the
    /// latter. This is the "controls don't announce their state change" gap.
    /// </remarks>
    private void RegisterPropertyChanges()
    {
        if (_automation is null || _cacheRequest is null)
        {
            return;
        }
        try
        {
            _propertySink = new PropertySink(this);
            // The SAFEARRAY overload needs a marshalled array; the
            // NativeArray variant takes a plain pointer and is simpler to
            // get right.
            Span<int> properties =
            [
                (int)UIA_PROPERTY_ID.UIA_ToggleToggleStatePropertyId,
                (int)UIA_PROPERTY_ID.UIA_ExpandCollapseExpandCollapseStatePropertyId,
                (int)UIA_PROPERTY_ID.UIA_ValueValuePropertyId,
                (int)UIA_PROPERTY_ID.UIA_RangeValueValuePropertyId,
                // Deliberately NOT SelectionItemIsSelected. ElementSelected
                // already reports a selection change, and registering both
                // meant one arrow press produced two announcements about the
                // same item through two different reasons — which then carried
                // two different cancel groups, so neither superseded the other.
                (int)UIA_PROPERTY_ID.UIA_NamePropertyId,
            ];
            unsafe
            {
                fixed (int* ptr = properties)
                {
                    _automation.AddPropertyChangedEventHandlerNativeArray(
                        _automation.GetRootElement(),
                        TreeScope.TreeScope_Subtree,
                        _cacheRequest,
                        _propertySink,
                        (UIA_PROPERTY_ID*)ptr,
                        properties.Length);
                }
            }
            _log.Debug("registered property-changed handler");
        }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
        {
            _log.Warning(ex, "could not register the property-changed handler");
        }
    }

    /// <summary>
    /// UIA 6 notifications — how modern applications announce transient status.
    /// There is no managed-client equivalent at all, which is why toasts were
    /// silent before this.
    /// </summary>
    [SupportedOSPlatform("windows10.0.17763.0")]
    private void RegisterNotifications()
    {
        if (_automation is not IUIAutomation6 six || _cacheRequest is null)
        {
            _log.Information("IUIAutomation6 unavailable; notification events will not be received");
            return;
        }
        try
        {
            _notificationSink = new NotificationSink(this);
            six.AddNotificationEventHandler(
                _automation.GetRootElement(), TreeScope.TreeScope_Subtree, _cacheRequest, _notificationSink);
            _log.Debug("registered notification handler");
        }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
        {
            _log.Warning(ex, "could not register the notification handler");
        }
    }

    // ---- sinks: minimum work, then queue -----------------------------------

    private void Queue(RawKind kind, IUIAutomationElement? element, string? text = null)
    {
        if (element is null)
        {
            return;
        }
        try
        {
            _events.Writer.TryWrite(new RawEvent(kind, element, text));
        }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
        {
            _log.Warning(ex, "ignored exception queueing {Kind}", kind);
        }
    }

    private sealed class FocusSink(NativeUiaProvider owner) : IUIAutomationFocusChangedEventHandler
    {
        public void HandleFocusChangedEvent(IUIAutomationElement sender)
            => owner.Queue(RawKind.Focus, sender);
    }

    private sealed class EventSink(NativeUiaProvider owner) : IUIAutomationEventHandler
    {
        public void HandleAutomationEvent(IUIAutomationElement sender, UIA_EVENT_ID eventId)
        {
            var kind = eventId switch
            {
                UIA_EVENT_ID.UIA_Text_TextSelectionChangedEventId => RawKind.CaretMoved,
                UIA_EVENT_ID.UIA_Text_TextChangedEventId => RawKind.Text,
                UIA_EVENT_ID.UIA_SelectionItem_ElementSelectedEventId => RawKind.Selection,
                UIA_EVENT_ID.UIA_LiveRegionChangedEventId => RawKind.LiveRegion,
                UIA_EVENT_ID.UIA_ToolTipOpenedEventId => RawKind.ToolTip,
                _ => RawKind.Alert,
            };
            owner.Queue(kind, sender);
        }
    }

    private sealed class PropertySink(NativeUiaProvider owner) : IUIAutomationPropertyChangedEventHandler
    {
        public void HandlePropertyChangedEvent(
            IUIAutomationElement sender, UIA_PROPERTY_ID propertyId, object newValue)
            => owner.Queue(RawKind.Value, sender);
    }

    private sealed class NotificationSink(NativeUiaProvider owner) : IUIAutomationNotificationEventHandler
    {
        public void HandleNotificationEvent(
            IUIAutomationElement sender,
            NotificationKind notificationKind,
            NotificationProcessing notificationProcessing,
            global::Windows.Win32.Foundation.BSTR displayString,
            global::Windows.Win32.Foundation.BSTR activityId)
        {
            // The notification carries its own text; the element may be a bare
            // container with no name at all.
            owner.Queue(RawKind.Notification, sender, displayString.ToString());
        }
    }

    // ---- dispatch -----------------------------------------------------------

    private async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var raw in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    Handle(raw);
                }
                catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
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

    private void Handle(RawEvent raw)
    {
        switch (raw.Kind)
        {
            case RawKind.Focus:
                HandleFocus(raw.Element);
                break;

            case RawKind.Value:
            case RawKind.Text:
                Emit(raw.Element, AccessibilityEventKind.ValueChanged, focusedOnly: true);
                break;

            case RawKind.CaretMoved:
                Emit(raw.Element, AccessibilityEventKind.CaretMoved, focusedOnly: true);
                break;

            case RawKind.Selection:
                Emit(raw.Element, AccessibilityEventKind.SelectionChanged, focusedOnly: false);
                break;

            case RawKind.LiveRegion:
                Emit(raw.Element, AccessibilityEventKind.LiveRegionChanged, focusedOnly: false);
                break;

            case RawKind.Alert:
                Emit(raw.Element, AccessibilityEventKind.AlertRaised, focusedOnly: false);
                break;

            case RawKind.ToolTip:
                Emit(raw.Element, AccessibilityEventKind.ToolTipOpened, focusedOnly: false);
                break;

            case RawKind.Notification:
                HandleNotification(raw.Element, raw.Text);
                break;
        }
    }

    private void HandleFocus(IUIAutomationElement element)
    {
        var node = NativeUiaNodeMapper.MapCached(element);
        if (node is null)
        {
            return;
        }
        node = EnrichSetPosition(node, element);
        if (node is null)
        {
            return;
        }

        var key = FocusKey(node);
        bool sameNode, sameControl;
        lock (_gate)
        {
            sameNode = _focused is { } prev && prev.Id == node.Id;
            sameControl = string.Equals(_lastFocusKey, key, StringComparison.Ordinal);
            _focused = node;
            _focusedElement = element;
            if (!sameNode)
            {
                _elementCache.Clear();
            }
            _elementCache[node.Id] = element;
            _lastFocusKey = key;
        }

        // The same control re-firing focus — common in editable combos, which
        // hand out a fresh runtime id on every arrow press. Already announced.
        if (sameNode || sameControl)
        {
            return;
        }

        _log.Debug("event FocusChanged on {Id} name={Name} role={Role}",
            node.Id.Value, Aura.Diagnostics.Redaction.Text(node.Name), node.Role);
        DispatchLocal(new AccessibilityEvent(AccessibilityEventKind.FocusChanged, node, DateTimeOffset.UtcNow));
    }

    private void HandleNotification(IUIAutomationElement element, string? text)
    {
        var node = NativeUiaNodeMapper.MapCached(element);
        if (node is null)
        {
            return;
        }
        // Prefer the notification's own text; the element is often an unnamed
        // container that would otherwise announce nothing.
        var spoken = string.IsNullOrWhiteSpace(text) ? node.Name : text;
        if (string.IsNullOrWhiteSpace(spoken))
        {
            return;
        }
        // Put it on the node rather than only in CaretLine: every rule that
        // announces a non-focus event reads {name}, so text carried anywhere
        // else was silently unreachable and toasts said nothing at all.
        DispatchLocal(new AccessibilityEvent(
            AccessibilityEventKind.LiveRegionChanged,
            new AccessibleNode(node.Id, node.Role, spoken, node.Value, node.Description,
                node.States, node.ParentId, node.ChildrenFactory, node.Extras),
            DateTimeOffset.UtcNow,
            CaretLine: spoken));
    }


    // Set position, when the provider does not publish it.
    /// <summary>
    /// Fill in "n of m" for a list or tree item whose provider does not publish
    /// <c>PositionInSet</c> and <c>SizeOfSet</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The desktop and Explorer's list view are the cases that matter: they are
    /// MSAA-backed, so UIA synthesises what it can and those two properties are
    /// simply absent. Every WPF and WinUI list publishes them, which is why
    /// counts were heard in the settings dialog and not on the desktop — an
    /// inconsistency the user has no way to attribute and no reason to accept.
    /// </para>
    /// <para>
    /// Position comes from the legacy child id, which is free — it is already in
    /// the cache request. The count is one <c>FindAll</c> against the parent,
    /// which is one round trip but marshals every sibling, so it is remembered
    /// against the parent's identity and reused for as long as the user stays in
    /// the same container. Arrowing a folder therefore costs one extra call on
    /// entry and none afterwards.
    /// </para>
    /// <para>
    /// It is not cached. A per-parent cache was the first shape of this and it
    /// was wrong twice over: a folder's contents change under it, and it hid
    /// the fact that the position lookup was failing on the desktop entirely.
    /// One FindAll per focus change on a list item is the honest cost; if it
    /// measures badly on a large folder, cache it then, with the measurement in
    /// hand.
    /// </para>
    /// </remarks>
    private AccessibleNode EnrichSetPosition(AccessibleNode node, IUIAutomationElement element)
    {
        if (_automation is null
            || node.Role is not (AccessibleRole.ListItem or AccessibleRole.TreeItem)
            || node.Extras.ContainsKey("uia.SizeOfSet"))
        {
            return node;
        }

        try
        {
            var parent = _automation.ControlViewWalker.GetParentElement(element);
            if (parent is null)
            {
                _log.Debug("set position: {Id} has no parent", node.Id.Value);
                return node;
            }

            var children = parent.FindAll(TreeScope.TreeScope_Children, _automation.CreateTrueCondition());
            var count = children?.Length ?? 0;
            if (count <= 0)
            {
                _log.Debug("set position: {Id} parent reported no children", node.Id.Value);
                return node;
            }

            // Position from the legacy child id where the provider offers one —
            // it is already cached, so it costs nothing. Where it does not,
            // find the element among the siblings we just fetched. The desktop
            // is the case that matters and it was silent because only the first
            // route was tried.
            var position = element.GetCurrentPropertyValue(
                UIA_PROPERTY_ID.UIA_LegacyIAccessibleChildIdPropertyId) as int? ?? 0;
            if (position <= 0 || position > count)
            {
                position = 0;
                for (var i = 0; i < count; i++)
                {
                    if (_automation.CompareElements(element, children!.GetElement(i)) != 0)
                    {
                        position = i + 1;
                        break;
                    }
                }
            }
            if (position <= 0)
            {
                _log.Debug("set position: {Id} not found among {Count} siblings", node.Id.Value, count);
                return node;
            }

            _log.Debug("set position: {Id} is {Position} of {Count}", node.Id.Value, position, count);
            var extras = new Dictionary<string, object?>(node.Extras, StringComparer.Ordinal)
            {
                ["uia.PositionInSet"] = position,
                ["uia.SizeOfSet"] = count,
            };
            return new AccessibleNode(node.Id, node.Role, node.Name, node.Value, node.Description,
                node.States, node.ParentId, node.ChildrenFactory, extras);
        }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
        {
            _log.Debug(ex, "set position: provider failed for {Id}", node.Id.Value);
            return node;
        }
    }

    private void Emit(IUIAutomationElement element, AccessibilityEventKind kind, bool focusedOnly)
    {
        if (focusedOnly && !IsFocused(element))
        {
            return;
        }
        var node = NativeUiaNodeMapper.MapCached(element);
        if (node is null)
        {
            return;
        }
        node = EnrichSetPosition(node, element);
        if (!focusedOnly && string.IsNullOrEmpty(node.Name) && string.IsNullOrEmpty(node.Value))
        {
            // Nothing to say is not worth interrupting for.
            return;
        }

        _log.Debug("event {Kind} on {Id} name={Name} role={Role}",
            kind, node.Id.Value, Aura.Diagnostics.Redaction.Text(node.Name), node.Role);
        DispatchLocal(new AccessibilityEvent(kind, node, DateTimeOffset.UtcNow));
    }

    private bool IsFocused(IUIAutomationElement candidate)
    {
        IUIAutomationElement? focused;
        lock (_gate)
        {
            focused = _focusedElement;
        }
        if (focused is null || _automation is null)
        {
            return false;
        }
        try
        {
            return _automation.CompareElements(candidate, focused) != 0;
        }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Identity of a focus target for de-duplication. Role and name alone are
    /// not enough — every unnamed control of the same role would match every
    /// other one, silencing a toolbar of icon buttons. The bounding rectangle
    /// is what tells them apart, and needs no time window.
    /// </summary>
    private static string FocusKey(AccessibleNode node)
    {
        const char Sep = '';
        return string.Concat(
            ((int)node.Role).ToString(CultureInfo.InvariantCulture), Sep,
            node.Name ?? string.Empty, Sep,
            Extra(node, "uia.AutomationId") ?? string.Empty, Sep,
            Extra(node, "uia.Bounds") ?? node.Id.Value);

        static string? Extra(AccessibleNode n, string key)
            => n.Extras.TryGetValue(key, out var raw) ? raw as string : null;
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
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _log.Warning(ex, "subscriber threw on {Kind}", ev.Kind);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            _automation?.RemoveAllEventHandlers();
        }
        catch (Exception ex) when (NativeUiaNodeMapper.IsProviderFailure(ex))
        {
            _log.Warning(ex, "ignored exception removing native UIA handlers");
        }

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
            _elementCache.Clear();
        }
        _focusSink = null;
        _eventSink = null;
        _notificationSink = null;
        _propertySink = null;
        _cacheRequest = null;
        _automation = null;
    }

    private sealed class Subscription(
        NativeUiaProvider owner,
        AccessibilityEventKind kinds,
        Action<AccessibilityEvent> handler) : IDisposable
    {
        public AccessibilityEventKind Kinds { get; } = kinds;
        public Action<AccessibilityEvent> Handler { get; } = handler;

        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._subscriptions.Remove(this);
            }
        }
    }
}
