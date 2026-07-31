using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Plugins;
using OpenReader.Abstractions.Speech;

namespace OpenReader.AppModules.Browser;

/// <summary>
/// First-party app module for Chromium-based browsers (Microsoft Edge,
/// Google Chrome). Subscribes to the host's accessibility tree while
/// attached and customises the announcement of the tab strip and the
/// address bar.
/// </summary>
/// <remarks>
/// <para>
/// The module is intentionally minimal: it demonstrates the
/// <see cref="IAppModule"/> contract end-to-end (process matching, attach,
/// rule registration, detach) with rules that are observable to a user
/// and useful as a smoke test for the plugin pipeline. Deeper integration
/// (browse-mode buffer, ARIA live regions specific to the browser) belongs
/// in a Phase-4 follow-up.
/// </para>
/// <para>
/// Only the executable name is matched. We deliberately do not check the
/// process path — Chrome/Edge updates frequently relocate themselves under
/// per-channel paths and we don't want a stale path to disable us.
/// </para>
/// </remarks>
public sealed class BrowserModule : IAppModule
{
    private static readonly string[] Executables = { "msedge.exe", "chrome.exe", "brave.exe" };

    private IDisposable? _tabRule;
    private IDisposable? _addressBarRule;

    public AppModuleManifest Manifest { get; } = new(
        Id: "openreader.appmodule.browser",
        DisplayName: "Browser",
        Version: new System.Version(0, 1, 0),
        ApiVersion: new System.Version(1, 0),
        Author: "OpenReader",
        Description: "Edge / Chrome tab and address-bar announcements.");

    public bool Matches(ProcessInfo process)
    {
        if (process is null)
        {
            return false;
        }
        foreach (var exe in Executables)
        {
            if (string.Equals(exe, process.ExecutableName, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public System.Threading.Tasks.ValueTask OnAttachAsync(IAppContext context, System.Threading.CancellationToken cancellationToken)
    {
        // Tab item: lift the tab title to the front of the announcement and
        // suppress the verbose Chromium "tab item ... selected" wording.
        _tabRule = context.RegisterSpeechRule(new SpeechRule(
            Id: "browser.tab",
            Priority: 100,
            Scope: new SpeechRuleScope(
                Role: AccessibleRole.Tab,
                AppExecutableName: context.Process.ExecutableName,
                Reason: SpeechReason.FocusChanged),
            Action: new SpeechRuleAction.Emit("{name}, tab")));

        // Address bar (Edit role with name "Address and search bar" / similar).
        _addressBarRule = context.RegisterSpeechRule(new SpeechRule(
            Id: "browser.addressbar",
            Priority: 90,
            Scope: new SpeechRuleScope(
                Role: AccessibleRole.Edit,
                AppExecutableName: context.Process.ExecutableName,
                Reason: SpeechReason.FocusChanged,
                TextRegex: "(?i)address|search"),
            Action: new SpeechRuleAction.Emit("address bar, {value}")));

        return default;
    }

    public System.Threading.Tasks.ValueTask OnDetachAsync(System.Threading.CancellationToken cancellationToken)
    {
        _tabRule?.Dispose(); _tabRule = null;
        _addressBarRule?.Dispose(); _addressBarRule = null;
        return default;
    }
}
