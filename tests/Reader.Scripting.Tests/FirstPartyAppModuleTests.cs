using FluentAssertions;
using OpenReader.Abstractions.Plugins;
using OpenReader.Abstractions.Speech;
using OpenReader.Scripting;
using OpenReader.TestKit;
using Xunit;

namespace OpenReader.Scripting.Tests;

/// <summary>
/// Verifies that the four first-party app modules (Browser, Explorer,
/// VS Code, Notepad++) load through <see cref="PluginHost"/> from the
/// real host's deployed <c>app-modules/</c> directory and attach to the
/// expected processes.
/// </summary>
/// <remarks>
/// The host's <c>CopyAppModules</c> target copies module DLLs and manifests
/// into <c>$(OutDir)/app-modules/&lt;id&gt;/</c>. We resolve that path
/// relative to this test assembly's location: walk up from
/// <c>tests/Reader.Scripting.Tests/bin/Debug/net8.0/</c> to the repo root
/// and into <c>src/ReaderHost.Windows/bin/Debug/...</c>.
/// </remarks>
public class FirstPartyAppModuleTests
{
    private static readonly string[] ExpectedManifestIds =
    {
        "openreader.appmodule.browser",
        "openreader.appmodule.explorer",
        "openreader.appmodule.vscode",
        "openreader.appmodule.notepad-plus-plus",
    };


    private static string ResolveAppModulesRoot()
    {
        // Find the repo root by walking up until we see "OpenReader.slnx".
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "OpenReader.slnx")))
            {
                break;
            }
            dir = Path.GetDirectoryName(dir);
        }
        if (string.IsNullOrEmpty(dir))
        {
            return string.Empty;
        }
        var hostBin = Path.Combine(dir, "src", "ReaderHost.Windows", "bin");
        if (!Directory.Exists(hostBin))
        {
            return string.Empty;
        }
        // Pick whatever Configuration\TFM directory exists with an app-modules folder.
        foreach (var config in Directory.EnumerateDirectories(hostBin))
        {
            foreach (var tfm in Directory.EnumerateDirectories(config))
            {
                var candidate = Path.Combine(tfm, "app-modules");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        return string.Empty;
    }

    private static ProcessInfo Process(string exe, string title) =>
        new(ProcessId: 1, ExecutableName: exe, ExecutablePath: null, WindowTitle: title, AppUserModelId: null);

    [Fact]
    public async Task All_four_first_party_modules_load()
    {
        var root = ResolveAppModulesRoot();
        Assert.False(string.IsNullOrEmpty(root), "ReaderHost.Windows app-modules output not found; run `dotnet build OpenReader.slnx` before testing.");

        using var provider = new SyntheticAccessibilityProvider(System.Array.Empty<OpenReader.Abstractions.Accessibility.AccessibleNode>());
        await using var host = new PluginHost(root, provider, _ => true);
        await host.LoadAllAsync();

        host.Plugins.Select(p => p.Manifest.Id).Should().BeEquivalentTo(ExpectedManifestIds);
    }

    [Theory]
    [InlineData("msedge.exe", "openreader.appmodule.browser")]
    [InlineData("chrome.exe", "openreader.appmodule.browser")]
    [InlineData("explorer.exe", "openreader.appmodule.explorer")]
    [InlineData("code.exe", "openreader.appmodule.vscode")]
    [InlineData("notepad++.exe", "openreader.appmodule.notepad-plus-plus")]
    public async Task Module_attaches_for_its_target_executable(string exe, string expectedId)
    {
        var root = ResolveAppModulesRoot();
        Assert.False(string.IsNullOrEmpty(root), "ReaderHost.Windows app-modules output not found; run `dotnet build OpenReader.slnx` before testing.");

        using var provider = new SyntheticAccessibilityProvider(System.Array.Empty<OpenReader.Abstractions.Accessibility.AccessibleNode>());
        var announcements = new List<SpeechRequest>();
        await using var host = new PluginHost(root, provider, r => { announcements.Add(r); return true; });
        await host.LoadAllAsync();
        await host.OnFocusChangedAsync(Process(exe, "test"));

        var attached = host.Plugins.Where(p => p.IsAttached).Select(p => p.Manifest.Id).ToArray();
        attached.Should().Contain(expectedId);
        // Each first-party module registers at least one rule on attach.
        host.CurrentRules.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Non_matching_process_attaches_nothing()
    {
        var root = ResolveAppModulesRoot();
        Assert.False(string.IsNullOrEmpty(root), "ReaderHost.Windows app-modules output not found; run `dotnet build OpenReader.slnx` before testing.");

        using var provider = new SyntheticAccessibilityProvider(System.Array.Empty<OpenReader.Abstractions.Accessibility.AccessibleNode>());
        await using var host = new PluginHost(root, provider, _ => true);
        await host.LoadAllAsync();
        await host.OnFocusChangedAsync(Process("calc.exe", "Calculator"));

        host.Plugins.Where(p => p.IsAttached).Should().BeEmpty();
        host.CurrentRules.Should().BeEmpty();
    }
}
