using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Plugins;
using Aura.Abstractions.Speech;

namespace Aura.AppModules.Explorer;

/// <summary>
/// First-party app module for File Explorer (<c>explorer.exe</c>). Adds
/// rules that announce selection changes in the file list with the item
/// type (folder / file) instead of the bare name, and suppresses the
/// noisy redundant "list item" role announcement that the default
/// Windows speech rules produce on focus.
/// </summary>
public sealed class ExplorerModule : IAppModule
{
    private IDisposable? _selectionRule;

    public AppModuleManifest Manifest { get; } = new(
        Id: "aura.appmodule.explorer",
        DisplayName: "Windows Explorer",
        Version: new System.Version(0, 1, 0),
        ApiVersion: new System.Version(1, 0),
        Author: "Aura",
        Description: "Selection announcement enrichment for File Explorer.");

    public bool Matches(ProcessInfo process)
        => process is not null
           && string.Equals(process.ExecutableName, "explorer.exe", System.StringComparison.OrdinalIgnoreCase);

    public System.Threading.Tasks.ValueTask OnAttachAsync(IAppContext context, System.Threading.CancellationToken cancellationToken)
    {
        _selectionRule = context.RegisterSpeechRule(new SpeechRule(
            Id: "explorer.selection",
            Priority: 80,
            Scope: new SpeechRuleScope(
                Role: AccessibleRole.ListItem,
                AppExecutableName: "explorer.exe",
                Reason: SpeechReason.SelectionChanged),
            Action: new SpeechRuleAction.Emit("{name}")));
        return default;
    }

    public System.Threading.Tasks.ValueTask OnDetachAsync(System.Threading.CancellationToken cancellationToken)
    {
        _selectionRule?.Dispose(); _selectionRule = null;
        return default;
    }
}
