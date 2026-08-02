using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Plugins;
using Aura.Abstractions.Speech;

namespace Aura.Samples.SamplePlugin;

/// <summary>
/// Sample plugin: emits "tab changed in Edge" on every Tab focus inside
/// Microsoft Edge. The roadmap's Phase-3 acceptance criterion uses this
/// exact behaviour as the proof point that an external developer can ship
/// a working plugin without recompiling Aura.
/// </summary>
/// <remarks>
/// Real plugins should pick a more interesting integration; this one is
/// short on purpose so newcomers can read it end-to-end.
/// </remarks>
public sealed class TabAnnouncerModule : IAppModule
{
    private IDisposable? _rule;

    public AppModuleManifest Manifest { get; } = new(
        Id: "aura.sample.tab-announcer",
        DisplayName: "Edge tab announcer (sample)",
        Version: new Version(0, 1, 0),
        ApiVersion: new Version(1, 0));

    public bool Matches(ProcessInfo process)
        => string.Equals(process.ExecutableName, "msedge.exe", StringComparison.OrdinalIgnoreCase);

    public ValueTask OnAttachAsync(IAppContext context, CancellationToken cancellationToken)
    {
        // Register a single rule that prefixes Edge tab announcements.
        _rule = context.RegisterSpeechRule(new SpeechRule(
            Id: "sample.edge.tab",
            Priority: 50,
            Scope: new SpeechRuleScope(
                Role: AccessibleRole.Tab,
                AppExecutableName: "msedge.exe",
                Reason: SpeechReason.FocusChanged),
            Action: new SpeechRuleAction.Emit("tab changed in Edge: {name}")));
        return default;
    }

    public ValueTask OnDetachAsync(CancellationToken cancellationToken)
    {
        _rule?.Dispose();
        _rule = null;
        return default;
    }
}
