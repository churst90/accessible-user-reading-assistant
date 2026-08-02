using System.Runtime.Versioning;
using FluentAssertions;
using Aura.Abstractions.Speech;
using Aura.Platform.Windows.Speech;
using Xunit;

namespace Aura.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public class ProsodyCompositionTests
{
    [Fact]
    public void Default_utterance_prosody_uses_user_default_rate()
    {
        // Regression: a rule emitting ProsodyHint.Default (rate=100) used to
        // make IsDefault=true and skip the user's preferred rate. The new
        // ComposeProsody multiplies rate ratios so a "neutral" 100% utterance
        // composed with a 225% user default gives 225%.
        var utterance = ProsodyHint.Default;
        var defaults = new ProsodyHint(PitchDelta: 0f, RatePercent: 225f, VolumeDelta: 0f);

        var composed = Sapi5Engine.ComposeProsody(utterance, defaults);

        composed.RatePercent.Should().Be(225f);
        composed.PitchDelta.Should().Be(0f);
        composed.VolumeDelta.Should().Be(0f);
    }

    [Fact]
    public void Pitch_only_modulation_preserves_user_default_rate()
    {
        // A rule that bumps pitch (e.g. capital-letter cue +6 semitones) but
        // doesn't touch rate should NOT clobber the user's rate.
        var utterance = new ProsodyHint(PitchDelta: 6f, RatePercent: 100f, VolumeDelta: 0f);
        var defaults = new ProsodyHint(PitchDelta: 0f, RatePercent: 200f, VolumeDelta: 0f);

        var composed = Sapi5Engine.ComposeProsody(utterance, defaults);

        composed.RatePercent.Should().Be(200f);
        composed.PitchDelta.Should().Be(6f);
    }

    [Fact]
    public void Rule_rate_modulation_multiplies_against_user_default()
    {
        // A rule saying "speak this 80% of normal" composed with a user rate
        // of 200% gives 160% (the rule slows things down relative to whatever
        // the user picked, not absolute).
        var utterance = new ProsodyHint(PitchDelta: 0f, RatePercent: 80f, VolumeDelta: 0f);
        var defaults = new ProsodyHint(PitchDelta: 0f, RatePercent: 200f, VolumeDelta: 0f);

        var composed = Sapi5Engine.ComposeProsody(utterance, defaults);

        composed.RatePercent.Should().Be(160f);
    }

    [Fact]
    public void Volume_and_pitch_deltas_add()
    {
        var utterance = new ProsodyHint(PitchDelta: 3f, RatePercent: 100f, VolumeDelta: -10f);
        var defaults = new ProsodyHint(PitchDelta: 1f, RatePercent: 100f, VolumeDelta: 5f);

        var composed = Sapi5Engine.ComposeProsody(utterance, defaults);

        composed.PitchDelta.Should().Be(4f);
        composed.VolumeDelta.Should().Be(-5f);
    }
}
