using System.Globalization;

namespace OpenReader.Abstractions.Speech;

/// <summary>Describes a voice exposed by an <see cref="ISpeechEngine"/>.</summary>
public sealed record VoiceInfo(
    string Id,
    string DisplayName,
    CultureInfo Culture,
    VoiceGender Gender,
    bool IsNeural);

public enum VoiceGender
{
    Unknown = 0,
    Male,
    Female,
    Neutral,
}
