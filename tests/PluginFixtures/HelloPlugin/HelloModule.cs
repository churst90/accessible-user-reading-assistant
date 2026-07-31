using OpenReader.Abstractions.Plugins;
using OpenReader.Abstractions.Speech;

namespace HelloPlugin;

/// <summary>
/// Tiny test-fixture plugin. Matches a process whose executable name is
/// "hello.exe", announces "hello from plugin" on attach, and registers a
/// rule so the host's rule-set integration path is exercised. Test
/// observability flows through the host's <c>announce</c> callback rather
/// than static state — the plugin runs in its own load context so its
/// statics aren't reachable from the test's load context.
/// </summary>
public sealed class HelloModule : IAppModule
{
    public AppModuleManifest Manifest { get; } = new(
        Id: "test.hello",
        DisplayName: "Hello plugin",
        Version: new System.Version(0, 1, 0),
        ApiVersion: new System.Version(1, 0));

    public bool Matches(ProcessInfo process)
        => string.Equals(process.ExecutableName, "hello.exe", System.StringComparison.OrdinalIgnoreCase);

    public System.Threading.Tasks.ValueTask OnAttachAsync(IAppContext context, System.Threading.CancellationToken cancellationToken)
    {
        context.RegisterSpeechRule(new SpeechRule(
            Id: "test.hello.rule",
            Priority: 1,
            Scope: new SpeechRuleScope(AppExecutableName: "hello.exe"),
            Action: new SpeechRuleAction.Emit("hello world")));
        return new System.Threading.Tasks.ValueTask(
            context.AnnounceAsync("hello from plugin", SpeechPriority.Next, cancellationToken).AsTask());
    }

    public System.Threading.Tasks.ValueTask OnDetachAsync(System.Threading.CancellationToken cancellationToken)
        => default;
}
