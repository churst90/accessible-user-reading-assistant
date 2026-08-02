namespace Aura.Abstractions.Accessibility;

/// <summary>
/// Immutable snapshot of an accessibility tree node at a moment in time.
/// </summary>
/// <remarks>
/// Children are produced lazily via <see cref="ChildrenFactory"/> so walking a
/// large tree only materializes nodes the consumer asks for. Parent traversal
/// is by id only — providers may not retain parent references in memory.
///
/// <para>
/// <b>Extras</b> is the platform escape hatch (UIA AutomationId, AT-SPI path,
/// IAccessible2 attributes). Core code should not consume Extras directly;
/// app modules may.
/// </para>
/// </remarks>
public sealed record AccessibleNode
{
    public AccessibleNode(
        NodeId id,
        AccessibleRole role,
        string? name,
        string? value,
        string? description,
        AccessibleStates states,
        NodeId? parentId,
        Func<IReadOnlyList<AccessibleNode>>? childrenFactory = null,
        IReadOnlyDictionary<string, object?>? extras = null)
    {
        Id = id;
        Role = role;
        Name = name;
        Value = value;
        Description = description;
        States = states;
        ParentId = parentId;
        ChildrenFactory = childrenFactory ?? EmptyChildrenFactory;
        Extras = extras ?? EmptyExtras;
    }

    public NodeId Id { get; }

    public AccessibleRole Role { get; }

    public string? Name { get; }

    public string? Value { get; }

    public string? Description { get; }

    public AccessibleStates States { get; }

    public NodeId? ParentId { get; }

    /// <summary>Lazy materialization of children. Call sites must cache results if reused.</summary>
    public Func<IReadOnlyList<AccessibleNode>> ChildrenFactory { get; }

    /// <summary>Platform-specific data. Discouraged for core; allowed for app modules.</summary>
    public IReadOnlyDictionary<string, object?> Extras { get; }

    public bool HasState(AccessibleStates state) => (States & state) == state;

    private static readonly Func<IReadOnlyList<AccessibleNode>> EmptyChildrenFactory = static () => Array.Empty<AccessibleNode>();
    private static readonly IReadOnlyDictionary<string, object?> EmptyExtras = new Dictionary<string, object?>(0);
}
