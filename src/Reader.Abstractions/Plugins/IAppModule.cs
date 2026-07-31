namespace OpenReader.Abstractions.Plugins;

/// <summary>
/// A pluggable per-application shim. The host loads and matches modules against
/// the focused process; matching modules receive attach / detach callbacks and
/// participate in event handling for the lifetime of that focus.
/// </summary>
/// <remarks>
/// Implementations must be parameterless-constructible; the host instantiates
/// them via the activator and configures them through <see cref="OnAttachAsync"/>.
/// </remarks>
public interface IAppModule
{
    AppModuleManifest Manifest { get; }

    /// <summary>True if this module wants to handle the given process.</summary>
    bool Matches(ProcessInfo process);

    /// <summary>Called when the host attaches this module to a matching process.</summary>
    ValueTask OnAttachAsync(IAppContext context, CancellationToken cancellationToken);

    /// <summary>Called when focus moves away from the process or the host shuts down.</summary>
    ValueTask OnDetachAsync(CancellationToken cancellationToken);
}
