using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Plugins;
using Aura.Abstractions.Speech;

namespace Aura.AppModules.VsCode;

/// <summary>
/// First-party app module for Visual Studio Code (<c>code.exe</c>). VS Code
/// uses an Electron / Chromium-derived UIA tree; the default speech rules
/// announce the editor's row of decoration glyphs verbosely. This shim
/// adds rewrite rules that compress those into a single readable phrase.
/// </summary>
public sealed class VsCodeModule : IAppModule
{
    private IDisposable? _editorRule;
    private IDisposable? _statusRule;

    public AppModuleManifest Manifest { get; } = new(
        Id: "aura.appmodule.vscode",
        DisplayName: "Visual Studio Code",
        Version: new System.Version(0, 1, 0),
        ApiVersion: new System.Version(1, 0),
        Author: "Aura",
        Description: "Editor and status-bar tuning for VS Code.");

    public bool Matches(ProcessInfo process)
        => process is not null
           && (string.Equals(process.ExecutableName, "code.exe", System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(process.ExecutableName, "code-insiders.exe", System.StringComparison.OrdinalIgnoreCase));

    public System.Threading.Tasks.ValueTask OnAttachAsync(IAppContext context, System.Threading.CancellationToken cancellationToken)
    {
        // The editor's Document role focused — emit a short identification
        // line. The platform's text provider supplies the line content
        // separately on review, so we only want a brief preface here.
        _editorRule = context.RegisterSpeechRule(new SpeechRule(
            Id: "vscode.editor",
            Priority: 70,
            Scope: new SpeechRuleScope(
                Role: AccessibleRole.Document,
                AppExecutableName: context.Process.ExecutableName,
                Reason: SpeechReason.FocusChanged),
            Action: new SpeechRuleAction.Emit("editor, {name}")));

        // The status bar lights up frequently with the line/column counter.
        // Suppress its focus event entirely; users can review-cursor it.
        _statusRule = context.RegisterSpeechRule(new SpeechRule(
            Id: "vscode.status.suppress",
            Priority: 60,
            Scope: new SpeechRuleScope(
                Role: AccessibleRole.StatusBar,
                AppExecutableName: context.Process.ExecutableName,
                Reason: SpeechReason.FocusChanged),
            Action: new SpeechRuleAction.Suppress()));

        return default;
    }

    public System.Threading.Tasks.ValueTask OnDetachAsync(System.Threading.CancellationToken cancellationToken)
    {
        _editorRule?.Dispose(); _editorRule = null;
        _statusRule?.Dispose(); _statusRule = null;
        return default;
    }
}
