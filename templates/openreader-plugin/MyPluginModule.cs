using OpenReader.Abstractions.Plugins;
using OpenReader.Abstractions.Speech;

namespace MyPlugin;

/// <summary>
/// Sample OpenReader app module. Edit Matches() to attach to a different
/// process and OnAttachAsync() to register your speech rules or trigger
/// announcements.
/// </summary>
public sealed class MyPluginModule : IAppModule
{
    public AppModuleManifest Manifest { get; } = new(
        Id: "PLUGIN_ID_PLACEHOLDER",
        DisplayName: "PLUGIN_DISPLAY_NAME_PLACEHOLDER",
        Version: new System.Version(0, 1, 0),
        ApiVersion: new System.Version(1, 0));

    public bool Matches(ProcessInfo process)
        => string.Equals(process.ExecutableName, "PLUGIN_TARGET_EXE_PLACEHOLDER", System.StringComparison.OrdinalIgnoreCase);

    public System.Threading.Tasks.ValueTask OnAttachAsync(IAppContext context, System.Threading.CancellationToken cancellationToken)
    {
        // Anything you announce here is queued through the host's speech pipeline.
        return new System.Threading.Tasks.ValueTask(
            context.AnnounceAsync("PLUGIN_DISPLAY_NAME_PLACEHOLDER attached", SpeechPriority.Next, cancellationToken).AsTask());
    }

    public System.Threading.Tasks.ValueTask OnDetachAsync(System.Threading.CancellationToken cancellationToken) => default;
}
