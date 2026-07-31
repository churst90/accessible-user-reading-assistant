namespace OpenReader.Speech.Punctuation;

/// <summary>
/// How much punctuation to spell out when speaking text. Higher levels
/// announce more punctuation literally; lower levels keep punctuation in
/// the text but rely on the engine's prosody to convey it (commas as pauses,
/// periods as sentence breaks, etc.).
/// </summary>
public enum PunctuationLevel
{
    /// <summary>Strip all punctuation — useful when reading messy text.</summary>
    None = 0,

    /// <summary>Keep sentence-shaping punctuation; drop the rest.</summary>
    Some,

    /// <summary>Spell out non-trivial punctuation but not common terminators.</summary>
    Most,

    /// <summary>Spell out every punctuation character.</summary>
    All,
}
