using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Plugins;
using Aura.Abstractions.Speech;

namespace Aura.AppModules.NotepadPlusPlus;

/// <summary>
/// First-party app module for Notepad++ (<c>notepad++.exe</c>). Adds rules
/// that announce tab switches with the document name and that suppress the
/// status-bar's frequent line/column updates.
/// </summary>
public sealed class NotepadPlusPlusModule : IAppModule
{
    private IDisposable? _tabRule;

    public AppModuleManifest Manifest { get; } = new(
        Id: "aura.appmodule.notepad-plus-plus",
        DisplayName: "Notepad++",
        Version: new System.Version(0, 1, 0),
        ApiVersion: new System.Version(1, 0),
        Author: "Aura",
        Description: "Tab announcement rules for Notepad++.");

    public bool Matches(ProcessInfo process)
        => process is not null
           && string.Equals(process.ExecutableName, "notepad++.exe", System.StringComparison.OrdinalIgnoreCase);

    public System.Threading.Tasks.ValueTask OnAttachAsync(IAppContext context, System.Threading.CancellationToken cancellationToken)
    {
        _tabRule = context.RegisterSpeechRule(new SpeechRule(
            Id: "npp.tab",
            Priority: 80,
            Scope: new SpeechRuleScope(
                Role: AccessibleRole.Tab,
                AppExecutableName: "notepad++.exe",
                Reason: SpeechReason.FocusChanged),
            Action: new SpeechRuleAction.Emit("{name}, document tab")));
        return default;
    }

    public System.Threading.Tasks.ValueTask OnDetachAsync(System.Threading.CancellationToken cancellationToken)
    {
        _tabRule?.Dispose(); _tabRule = null;
        return default;
    }
}
