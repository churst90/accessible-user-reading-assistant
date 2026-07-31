using OpenReader.Abstractions.Accessibility;

namespace OpenReader.TestKit;

/// <summary>
/// In-memory <see cref="IAccessibilityProvider"/> backed by a fixed list of
/// nodes. Tests drive focus and other events explicitly via the
/// <c>Simulate*</c> methods.
/// </summary>
public sealed class SyntheticAccessibilityProvider : IAccessibilityProvider, IDisposable
{
    private readonly Dictionary<NodeId, AccessibleNode> _byId;
    private readonly List<Subscription> _subscriptions = new();
    private readonly object _gate = new();
    private NodeId? _focusedId;

    public SyntheticAccessibilityProvider(IReadOnlyList<AccessibleNode> nodes)
    {
        _byId = nodes.ToDictionary(n => n.Id);
        Root = nodes.FirstOrDefault(n => n.ParentId is null);
    }

    public AccessibleNode? Focused => _focusedId.HasValue && _byId.TryGetValue(_focusedId.Value, out var n) ? n : null;

    public AccessibleNode? Root { get; }

    public AccessibleNode? FromPoint(int screenX, int screenY) => null;

    public IDisposable Subscribe(AccessibilityEventKind kinds, Action<AccessibilityEvent> handler)
    {
        var sub = new Subscription(this, kinds, handler);
        lock (_gate)
        {
            _subscriptions.Add(sub);
        }
        return sub;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _subscriptions.Clear();
        }
    }

    /// <summary>Simulate focus moving to the named node (first match by name).</summary>
    public void SimulateFocus(string nodeName)
    {
        var node = _byId.Values.FirstOrDefault(n => n.Name == nodeName)
            ?? throw new InvalidOperationException($"No node named '{nodeName}' in synthetic tree.");
        SimulateFocus(node.Id);
    }

    /// <summary>Simulate focus moving to a specific node id.</summary>
    public void SimulateFocus(NodeId id)
    {
        _focusedId = id;
        Raise(new AccessibilityEvent(AccessibilityEventKind.FocusChanged, _byId[id], DateTimeOffset.UtcNow));
    }

    /// <summary>Simulate an arbitrary event for a named node.</summary>
    public void SimulateEvent(AccessibilityEventKind kind, string nodeName)
    {
        var node = _byId.Values.First(n => n.Name == nodeName);
        Raise(new AccessibilityEvent(kind, node, DateTimeOffset.UtcNow));
    }

    private void Raise(AccessibilityEvent ev)
    {
        Subscription[] snapshot;
        lock (_gate)
        {
            snapshot = _subscriptions.ToArray();
        }
        foreach (var sub in snapshot)
        {
            if ((sub.Kinds & ev.Kind) != 0)
            {
                sub.Handler(ev);
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly SyntheticAccessibilityProvider _owner;
        public AccessibilityEventKind Kinds { get; }
        public Action<AccessibilityEvent> Handler { get; }

        public Subscription(SyntheticAccessibilityProvider owner, AccessibilityEventKind kinds, Action<AccessibilityEvent> handler)
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
