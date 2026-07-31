using FluentAssertions;
using OpenReader.Abstractions.Plugins;
using OpenReader.Abstractions.Speech;
using OpenReader.Scripting;
using OpenReader.TestKit;
using Xunit;

namespace OpenReader.Scripting.Tests;

/// <summary>
/// End-to-end tests for <see cref="PluginHost"/>. Set up a real plugin
/// directory on disk (using the HelloPlugin fixture DLL) and exercise the
/// load → match → attach → detach → unload pipeline.
/// </summary>
public class PluginHostTests : IDisposable
{
    private readonly string _root;
    private readonly SyntheticAccessibilityProvider _provider;
    private readonly List<SpeechRequest> _announcements = new();

    public PluginHostTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "openreader-plugin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new SyntheticAccessibilityProvider(System.Array.Empty<OpenReader.Abstractions.Accessibility.AccessibleNode>());
    }

    public void Dispose()
    {
        _provider.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* swallow */ }
        GC.SuppressFinalize(this);
    }

    private string DeployHelloPlugin(string folderName = "hello")
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "HelloPlugin", "HelloPlugin.dll");
        File.Exists(fixturePath).Should().BeTrue($"fixture should be at {fixturePath}");

        var pluginDir = Path.Combine(_root, folderName);
        Directory.CreateDirectory(pluginDir);
        File.Copy(fixturePath, Path.Combine(pluginDir, "HelloPlugin.dll"), overwrite: true);

        File.WriteAllText(Path.Combine(pluginDir, "manifest.json"), """
            {
              "id": "test.hello",
              "displayName": "Hello",
              "version": "0.1.0",
              "apiVersion": "1.0",
              "assembly": "HelloPlugin.dll",
              "moduleType": "HelloPlugin.HelloModule"
            }
            """);
        return pluginDir;
    }

    private static ProcessInfo MatchingProcess() =>
        new(ProcessId: 1234, ExecutableName: "hello.exe", ExecutablePath: null, WindowTitle: null, AppUserModelId: null);

    private static ProcessInfo NonMatchingProcess() =>
        new(ProcessId: 5678, ExecutableName: "notepad.exe", ExecutablePath: null, WindowTitle: null, AppUserModelId: null);

    [Fact]
    public async Task LoadAllAsync_loads_plugin_from_disk()
    {
        DeployHelloPlugin();
        await using var host = new PluginHost(_root, _provider, Announce);
        await host.LoadAllAsync();

        host.Plugins.Should().ContainSingle()
            .Which.Manifest.Id.Should().Be("test.hello");
        host.Plugins.Single().IsAttached.Should().BeFalse();
    }

    [Fact]
    public async Task OnFocusChanged_attaches_a_matching_plugin()
    {
        DeployHelloPlugin();
        await using var host = new PluginHost(_root, _provider, Announce);
        await host.LoadAllAsync();

        await host.OnFocusChangedAsync(MatchingProcess());

        host.Plugins.Single().IsAttached.Should().BeTrue();
        // HelloModule announces "hello from plugin" on attach.
        _announcements.Should().Contain(r => r.RawText == "hello from plugin");
        // And it registers a SpeechRule, so CurrentRules is non-empty.
        host.CurrentRules.Should().ContainSingle()
            .Which.Id.Should().Be("test.hello.rule");
    }

    [Fact]
    public async Task OnFocusChanged_does_not_attach_for_non_matching_process()
    {
        DeployHelloPlugin();
        await using var host = new PluginHost(_root, _provider, Announce);
        await host.LoadAllAsync();

        await host.OnFocusChangedAsync(NonMatchingProcess());

        host.Plugins.Single().IsAttached.Should().BeFalse();
        _announcements.Should().BeEmpty();
        host.CurrentRules.Should().BeEmpty();
    }

    [Fact]
    public async Task Detach_fires_when_focus_leaves_the_matching_process()
    {
        DeployHelloPlugin();
        await using var host = new PluginHost(_root, _provider, Announce);
        await host.LoadAllAsync();

        await host.OnFocusChangedAsync(MatchingProcess());
        host.Plugins.Single().IsAttached.Should().BeTrue();

        await host.OnFocusChangedAsync(NonMatchingProcess());
        host.Plugins.Single().IsAttached.Should().BeFalse();
        host.CurrentRules.Should().BeEmpty();
    }

    [Fact]
    public async Task RulesChanged_fires_on_attach_and_detach()
    {
        DeployHelloPlugin();
        await using var host = new PluginHost(_root, _provider, Announce);
        await host.LoadAllAsync();

        var counter = 0;
        host.RulesChanged += () => counter++;

        await host.OnFocusChangedAsync(MatchingProcess());
        await host.OnFocusChangedAsync(NonMatchingProcess());

        counter.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Plugin_with_incompatible_api_is_refused()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "HelloPlugin", "HelloPlugin.dll");
        var pluginDir = Path.Combine(_root, "future");
        Directory.CreateDirectory(pluginDir);
        File.Copy(fixturePath, Path.Combine(pluginDir, "HelloPlugin.dll"));
        // Manifest declares a future major.
        File.WriteAllText(Path.Combine(pluginDir, "manifest.json"), $$"""
            {
              "id": "test.future",
              "version": "0.1.0",
              "apiVersion": "{{PluginApi.CurrentApiVersion.Major + 1}}.0",
              "assembly": "HelloPlugin.dll",
              "moduleType": "HelloPlugin.HelloModule"
            }
            """);

        await using var host = new PluginHost(_root, _provider, Announce);
        await host.LoadAllAsync();

        host.Plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task Plugin_with_invalid_manifest_is_skipped()
    {
        var pluginDir = Path.Combine(_root, "broken");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "manifest.json"), "{ this is not json");

        await using var host = new PluginHost(_root, _provider, Announce);
        await host.LoadAllAsync();

        host.Plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task Folders_without_a_manifest_are_ignored()
    {
        var pluginDir = Path.Combine(_root, "no-manifest");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "junk.txt"), "hi");

        await using var host = new PluginHost(_root, _provider, Announce);
        await host.LoadAllAsync();

        host.Plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task ReloadAsync_drops_plugins_whose_manifest_disappears()
    {
        // Note: we delete the manifest, not the assembly. The plugin assembly
        // is mmap'd by the ALC until GC unloads it, so deleting the DLL while
        // the host is alive would race the OS file lock. Disabling a plugin
        // by removing its manifest is the supported "uninstall while running"
        // path; users who delete the whole folder do it between sessions.
        var pluginDir = DeployHelloPlugin();
        await using var host = new PluginHost(_root, _provider, Announce);
        await host.LoadAllAsync();
        host.Plugins.Should().ContainSingle();

        File.Delete(Path.Combine(pluginDir, "manifest.json"));
        await host.ReloadAsync();

        host.Plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task ReloadAsync_picks_up_newly_added_plugin()
    {
        await using var host = new PluginHost(_root, _provider, Announce);
        await host.LoadAllAsync();
        host.Plugins.Should().BeEmpty();

        DeployHelloPlugin();
        await host.ReloadAsync();

        host.Plugins.Should().ContainSingle()
            .Which.Manifest.Id.Should().Be("test.hello");
    }

    [Fact]
    public async Task ReloadAsync_re_evaluates_match_for_current_focus()
    {
        await using var host = new PluginHost(_root, _provider, Announce);
        await host.LoadAllAsync();

        // Deliver focus first, then deploy + reload — the host should re-attach.
        await host.OnFocusChangedAsync(MatchingProcess());
        DeployHelloPlugin();
        await host.ReloadAsync();

        host.Plugins.Single().IsAttached.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_unloads_attached_plugins()
    {
        DeployHelloPlugin();
        var host = new PluginHost(_root, _provider, Announce);
        await host.LoadAllAsync();
        await host.OnFocusChangedAsync(MatchingProcess());
        host.Plugins.Single().IsAttached.Should().BeTrue();

        await host.DisposeAsync();
        host.Plugins.Should().BeEmpty();
    }

    private bool Announce(SpeechRequest request)
    {
        _announcements.Add(request);
        return true;
    }
}
