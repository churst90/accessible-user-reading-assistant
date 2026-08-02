using Aura.Abstractions.Accessibility;

namespace Aura.TestKit;

/// <summary>
/// Fluent builder for constructing fake accessibility trees in tests without
/// launching real applications. Produces a flat list of <see cref="AccessibleNode"/>
/// suitable for feeding into <see cref="SyntheticAccessibilityProvider"/>.
/// </summary>
/// <example>
/// <code>
/// var tree = new SyntheticTreeBuilder()
///     .Window("Notepad", w => w
///         .MenuBar(m => m
///             .Menu("File", f => f
///                 .MenuItem("Open"))))
///     .Build();
/// </code>
/// </example>
public sealed class SyntheticTreeBuilder
{
    private readonly List<AccessibleNode> _nodes = new();
    private readonly NodeScope _root;
    private int _nextId = 1;

    public SyntheticTreeBuilder()
    {
        _root = new NodeScope(this, parentId: null);
    }

    public SyntheticTreeBuilder Window(string name, Action<NodeScope>? configure = null)
    {
        _root.Add(AccessibleRole.Window, name, configure);
        return this;
    }

    public IReadOnlyList<AccessibleNode> Build() => _nodes.ToArray();

    private NodeId NextId() => new($"synthetic-{_nextId++}");

    public sealed class NodeScope
    {
        private readonly SyntheticTreeBuilder _owner;
        private readonly NodeId? _parentId;
        private readonly List<NodeId> _childIds = new();

        internal NodeScope(SyntheticTreeBuilder owner, NodeId? parentId)
        {
            _owner = owner;
            _parentId = parentId;
        }

        public NodeScope Add(AccessibleRole role, string? name, Action<NodeScope>? configure = null, AccessibleStates states = AccessibleStates.None, string? value = null)
        {
            var id = _owner.NextId();
            var childScope = new NodeScope(_owner, id);
            configure?.Invoke(childScope);

            var capturedChildren = childScope._childIds.ToArray();
            var node = new AccessibleNode(
                id: id,
                role: role,
                name: name,
                value: value,
                description: null,
                states: states,
                parentId: _parentId,
                childrenFactory: () => capturedChildren.Select(_owner.Find).ToArray(),
                extras: null);

            _owner._nodes.Add(node);
            _childIds.Add(id);
            return this;
        }

        public NodeScope MenuBar(Action<NodeScope>? configure = null) => Add(AccessibleRole.MenuBar, null, configure);
        public NodeScope Menu(string name, Action<NodeScope>? configure = null) => Add(AccessibleRole.Menu, name, configure);
        public NodeScope MenuItem(string name) => Add(AccessibleRole.MenuItem, name);
        public NodeScope Button(string name, AccessibleStates states = AccessibleStates.Focusable) => Add(AccessibleRole.Button, name, states: states);
        public NodeScope Edit(string name, string? value = null, AccessibleStates states = AccessibleStates.Focusable | AccessibleStates.Editable) => Add(AccessibleRole.Edit, name, states: states, value: value);
        public NodeScope CheckBox(string name, bool isChecked = false) => Add(AccessibleRole.CheckBox, name, states: AccessibleStates.Focusable | (isChecked ? AccessibleStates.Checked : AccessibleStates.None));
        public NodeScope StaticText(string text) => Add(AccessibleRole.StaticText, text);
    }

    private AccessibleNode Find(NodeId id) => _nodes.First(n => n.Id == id);
}
