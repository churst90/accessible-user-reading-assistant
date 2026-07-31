namespace OpenReader.Abstractions.Accessibility;

/// <summary>
/// Opaque identifier for an accessibility node, scoped to a single provider session.
/// </summary>
/// <remarks>
/// Platforms generate these however they like (UIA runtime IDs, AT-SPI paths, synthetic GUIDs).
/// Equality is structural over <see cref="Value"/>. Comparison across providers is meaningless.
/// </remarks>
public readonly record struct NodeId(string Value)
{
    public static NodeId Empty => new(string.Empty);

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public override string ToString() => Value;
}
