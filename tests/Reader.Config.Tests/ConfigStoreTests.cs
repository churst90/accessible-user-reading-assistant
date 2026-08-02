using FluentAssertions;
using Aura.Config;
using Xunit;

namespace Aura.Config.Tests;

public class ConfigStoreTests
{
    [Fact]
    public void Defaults_layer_yields_default_speech()
    {
        using var store = new ConfigStore();
        store.AddLayer("defaults", ReaderConfig.Defaults());

        store.Current.Speech!.Engine.Should().Be("sapi5");
        store.Current.Speech.RatePercent.Should().Be(100f);
    }

    [Fact]
    public void Adding_layer_raises_changed()
    {
        using var store = new ConfigStore();
        store.AddLayer("defaults", ReaderConfig.Defaults());

        ReaderConfig? observed = null;
        store.Changed += c => observed = c;

        store.AddLayer("user", new ReaderConfig { Speech = new SpeechConfig { RatePercent = 175f } });

        observed.Should().NotBeNull();
        observed!.Speech!.RatePercent.Should().Be(175f);
    }

    [Fact]
    public void File_layer_loads_from_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aura-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        try
        {
            File.WriteAllText(path, """{ "speech": { "ratePercent": 200 } }""");

            using var store = new ConfigStore();
            store.AddLayer("defaults", ReaderConfig.Defaults());
            store.AddFileLayer("user", path);

            store.Current.Speech!.RatePercent.Should().Be(200f);
            store.Current.Speech.Engine.Should().Be("sapi5"); // inherited
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_is_treated_as_empty_layer()
    {
        var path = Path.Combine(Path.GetTempPath(), "aura-missing-" + Guid.NewGuid().ToString("N") + ".json");

        using var store = new ConfigStore();
        store.AddLayer("defaults", ReaderConfig.Defaults());
        store.AddFileLayer("user", path);

        store.Current.Speech!.Engine.Should().Be("sapi5");
    }

    [Fact]
    public void RemoveLayer_drops_a_named_layer()
    {
        using var store = new ConfigStore();
        store.AddLayer("defaults", ReaderConfig.Defaults());
        store.AddLayer("user", new ReaderConfig { Speech = new SpeechConfig { RatePercent = 200f } });
        store.Current.Speech!.RatePercent.Should().Be(200f);

        store.HasLayer("user").Should().BeTrue();
        var removed = store.RemoveLayer("user");

        removed.Should().BeTrue();
        store.HasLayer("user").Should().BeFalse();
        store.Current.Speech!.RatePercent.Should().Be(100f);
    }

    [Fact]
    public void RemoveLayer_returns_false_when_layer_missing()
    {
        using var store = new ConfigStore();
        store.AddLayer("defaults", ReaderConfig.Defaults());

        store.RemoveLayer("nothing").Should().BeFalse();
    }

    [Fact]
    public void InsertFileLayer_places_layer_before_named_anchor()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aura-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var profilePath = Path.Combine(dir, "profile.json");
        try
        {
            File.WriteAllText(profilePath, """{ "speech": { "ratePercent": 175 } }""");

            using var store = new ConfigStore();
            store.AddLayer("defaults", ReaderConfig.Defaults());
            store.AddLayer("app", new ReaderConfig { Speech = new SpeechConfig { RatePercent = 300f } });

            // app layer wins for now.
            store.Current.Speech!.RatePercent.Should().Be(300f);

            // Profile inserted *before* app — app should still win.
            store.InsertFileLayer("profile", profilePath, beforeLayerName: "app");

            store.HasLayer("profile").Should().BeTrue();
            store.Current.Speech!.RatePercent.Should().Be(300f); // app still topmost

            // Drop app — profile now takes effect.
            store.RemoveLayer("app");
            store.Current.Speech!.RatePercent.Should().Be(175f);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void InsertFileLayer_appends_when_anchor_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aura-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        try
        {
            File.WriteAllText(path, """{ "speech": { "ratePercent": 250 } }""");

            using var store = new ConfigStore();
            store.AddLayer("defaults", ReaderConfig.Defaults());
            store.InsertFileLayer("user", path, beforeLayerName: "does-not-exist");

            store.Current.Speech!.RatePercent.Should().Be(250f);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void File_change_triggers_reload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aura-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        try
        {
            File.WriteAllText(path, """{ "speech": { "ratePercent": 100 } }""");

            using var store = new ConfigStore();
            store.AddLayer("defaults", ReaderConfig.Defaults());
            store.AddFileLayer("user", path);
            store.Current.Speech!.RatePercent.Should().Be(100f);

            using var changedEvent = new ManualResetEventSlim(false);
            store.Changed += _ => changedEvent.Set();
            File.WriteAllText(path, """{ "speech": { "ratePercent": 250 } }""");

            changedEvent.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
            store.Current.Speech!.RatePercent.Should().Be(250f);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
