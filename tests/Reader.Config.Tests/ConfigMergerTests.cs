using FluentAssertions;
using OpenReader.Config;
using Xunit;

namespace OpenReader.Config.Tests;

public class ConfigMergerTests
{
    [Fact]
    public void Empty_layers_yield_empty_config()
    {
        var merged = ConfigMerger.Merge();

        merged.Speech.Should().BeNull();
        merged.Input.Should().BeNull();
    }

    [Fact]
    public void Defaults_pass_through_when_no_overrides()
    {
        var merged = ConfigMerger.Merge(ReaderConfig.Defaults());

        merged.Speech!.Engine.Should().Be("sapi5");
        merged.Speech.RatePercent.Should().Be(100f);
    }

    [Fact]
    public void User_layer_overrides_only_specified_fields()
    {
        var defaults = ReaderConfig.Defaults();
        var user = new ReaderConfig
        {
            Speech = new SpeechConfig { RatePercent = 175f },
        };

        var merged = ConfigMerger.Merge(defaults, user);

        merged.Speech!.RatePercent.Should().Be(175f);
        merged.Speech.Engine.Should().Be("sapi5");      // inherited
        merged.Speech.VolumeDelta.Should().Be(0f);     // inherited
    }

    [Fact]
    public void App_layer_overrides_user_layer()
    {
        var defaults = ReaderConfig.Defaults();
        var user = new ReaderConfig { Speech = new SpeechConfig { RatePercent = 150f } };
        var app = new ReaderConfig { Speech = new SpeechConfig { RatePercent = 200f } };

        var merged = ConfigMerger.Merge(defaults, user, app);

        merged.Speech!.RatePercent.Should().Be(200f);
    }

    [Fact]
    public void Key_bindings_merge_with_last_write_wins_per_key()
    {
        var lower = new ReaderConfig
        {
            Input = new InputConfig
            {
                KeyBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Reader+Down"] = "ReadNextLine",
                    ["Reader+Up"] = "ReadPreviousLine",
                },
            },
        };
        var upper = new ReaderConfig
        {
            Input = new InputConfig
            {
                KeyBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Reader+Down"] = "SayAll",
                    ["Reader+T"] = "ReportTitle",
                },
            },
        };

        var merged = ConfigMerger.Merge(lower, upper);

        merged.Input!.KeyBindings!["Reader+Down"].Should().Be("SayAll");
        merged.Input.KeyBindings["Reader+Up"].Should().Be("ReadPreviousLine");
        merged.Input.KeyBindings["Reader+T"].Should().Be("ReportTitle");
    }

    [Fact]
    public void Null_layers_are_skipped()
    {
        var defaults = ReaderConfig.Defaults();

        var merged = ConfigMerger.Merge(null, defaults, null);

        merged.Speech!.Engine.Should().Be("sapi5");
    }
}
