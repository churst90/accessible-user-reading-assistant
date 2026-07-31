namespace OpenReader.Abstractions.Speech;

/// <summary>
/// Per-utterance prosody adjustments. Values are <em>deltas</em> from the
/// engine's current voice defaults so rules remain portable across voices.
/// </summary>
/// <param name="PitchDelta">Pitch adjustment in semitones, typical range [-12, +12].</param>
/// <param name="RatePercent">Rate as percent of default, typical range [25, 400]. 100 means default.</param>
/// <param name="VolumeDelta">Volume adjustment as percent of default, typical range [-100, +100].</param>
public readonly record struct ProsodyHint(
    float PitchDelta = 0f,
    float RatePercent = 100f,
    float VolumeDelta = 0f)
{
    /// <summary>
    /// The neutral prosody: no pitch or volume change, normal rate.
    /// </summary>
    /// <remarks>
    /// Built explicitly via the primary constructor — <c>new ProsodyHint()</c>
    /// (parameterless) on a record struct does <em>zero-init</em>, not
    /// primary-constructor-with-defaults, so it would produce
    /// <c>RatePercent = 0</c> (which when fed to a SAPI log mapping
    /// collapses to "very slow"). This was the long-standing "speech rate
    /// slider does nothing" bug.
    /// </remarks>
    public static ProsodyHint Default => new(0f, 100f, 0f);

    public bool IsDefault => PitchDelta == 0f && RatePercent == 100f && VolumeDelta == 0f;
}
