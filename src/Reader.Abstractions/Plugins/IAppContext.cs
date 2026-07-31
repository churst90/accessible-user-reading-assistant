using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Speech;

namespace OpenReader.Abstractions.Plugins;

/// <summary>
/// The narrow surface of host services exposed to an <see cref="IAppModule"/>.
/// Modules cannot reach into core internals — they go through this contract.
/// </summary>
/// <remarks>
/// Adding members here is a public API change. Removing or modifying members
/// requires an <see cref="AppModuleManifest.ApiVersion"/> major bump.
/// </remarks>
public interface IAppContext
{
    /// <summary>Process this module is currently attached to.</summary>
    ProcessInfo Process { get; }

    /// <summary>The accessibility tree, scoped to this process where possible.</summary>
    IAccessibilityProvider Accessibility { get; }

    /// <summary>Queue speech directly. Bypasses normal focus-event composition; use sparingly.</summary>
    ValueTask AnnounceAsync(string text, SpeechPriority priority = SpeechPriority.Next, CancellationToken cancellationToken = default);

    /// <summary>Register a speech rule contributed by this module. Removed automatically on detach.</summary>
    IDisposable RegisterSpeechRule(SpeechRule rule);
}
