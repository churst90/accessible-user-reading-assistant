using FluentAssertions;
using OpenReader.Config;
using Xunit;

namespace OpenReader.Config.Tests;

public class AppLayerSwitcherTests
{
    [Fact]
    public void Missing_app_override_leaves_lower_layer_intact()
    {
        using var store = new ConfigStore();
        store.AddLayer("defaults", ReaderConfig.Defaults());
        using var switcher = new AppLayerSwitcher(store);

        switcher.SwitchTo("nonexistent.exe");

        store.Current.Speech!.RatePercent.Should().Be(100f);
    }

    [Fact]
    public void App_override_layers_on_top()
    {
        // Place an app override file in a per-user-AppData layout, then point AppLayerSwitcher at it.
        var exe = "openreader-test-app-" + Guid.NewGuid().ToString("N")[..8] + ".exe";
        var path = ConfigPaths.AppConfigPath(exe);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, """{ "speech": { "ratePercent": 250 } }""");

            using var store = new ConfigStore();
            store.AddLayer("defaults", ReaderConfig.Defaults());
            using var switcher = new AppLayerSwitcher(store);

            switcher.SwitchTo(exe);
            store.Current.Speech!.RatePercent.Should().Be(250f);

            // Switching to a different (missing) app drops back to defaults.
            switcher.SwitchTo("other.exe");
            store.Current.Speech!.RatePercent.Should().Be(100f);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Switching_to_same_exe_is_a_noop()
    {
        using var store = new ConfigStore();
        store.AddLayer("defaults", ReaderConfig.Defaults());
        using var switcher = new AppLayerSwitcher(store);

        var changes = 0;
        store.Changed += _ => changes++;

        switcher.SwitchTo("same.exe");
        switcher.SwitchTo("same.exe");

        // Each SwitchTo() that detects the same exe early-returns before Reload.
        // The first call still fires Reload internally; the second should not.
        changes.Should().BeLessOrEqualTo(1);
    }
}
