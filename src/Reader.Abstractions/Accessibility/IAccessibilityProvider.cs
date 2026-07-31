namespace OpenReader.Abstractions.Accessibility;

/// <summary>
/// The single seam between the platform layer and core. Implementations expose
/// a normalized accessibility tree and event stream regardless of underlying
/// API (UIA on Windows, AT-SPI on Linux, etc.).
/// </summary>
/// <remarks>
/// <para>
/// Implementations must not leak platform-native types through this interface.
/// All return values are <see cref="AccessibleNode"/> or other types defined in
/// this assembly.
/// </para>
/// <para>
/// All members may be called from any thread. Implementations are responsible
/// for thread-safety; consumers should not assume callbacks run on a specific
/// thread (use a channel or marshal to your dispatcher).
/// </para>
/// </remarks>
public interface IAccessibilityProvider : IAsyncDisposable
{
    /// <summary>The currently focused node, or null if focus is unknown.</summary>
    AccessibleNode? Focused { get; }

    /// <summary>The desktop / root node.</summary>
    AccessibleNode? Root { get; }

    /// <summary>Hit-test for the node at a screen coordinate.</summary>
    AccessibleNode? FromPoint(int screenX, int screenY);

    /// <summary>
    /// Subscribe to events of the specified kinds. The returned disposable
    /// unsubscribes when disposed. Multiple concurrent subscriptions are allowed.
    /// </summary>
    IDisposable Subscribe(AccessibilityEventKind kinds, Action<AccessibilityEvent> handler);

    /// <summary>Start the provider. Must be called before events are raised.</summary>
    ValueTask StartAsync(CancellationToken cancellationToken);
}
