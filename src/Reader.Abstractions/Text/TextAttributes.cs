namespace Aura.Abstractions.Text;

/// <summary>
/// Well-known keys for <see cref="ITextRange.GetAttributes"/>. Backends map
/// their native attribute identifiers onto these; anything without a neutral
/// equivalent goes through under a <c>"uia."</c> / <c>"atspi."</c> prefix and
/// is only meaningful to an app module.
/// </summary>
/// <remarks>
/// Strings rather than an enum so a plugin can introduce an attribute without
/// a contract bump — the same trade-off <see cref="Accessibility.AccessibleNode.Extras"/>
/// already makes.
/// </remarks>
public static class TextAttributes
{
    /// <summary>Heading depth as an <c>int</c>, 1-6. Absent when not a heading.</summary>
    public const string HeadingLevel = "headingLevel";

    /// <summary>Link target as a <c>string</c>. Presence alone means "this is a link".</summary>
    public const string Link = "link";

    /// <summary><c>bool</c> — bold.</summary>
    public const string Bold = "bold";

    /// <summary><c>bool</c> — italic.</summary>
    public const string Italic = "italic";

    /// <summary><c>bool</c> — underlined.</summary>
    public const string Underline = "underline";

    /// <summary>BCP-47 language tag as a <c>string</c>. Drives per-language voice switching.</summary>
    public const string Language = "language";

    /// <summary>List nesting depth as an <c>int</c>, 1-based.</summary>
    public const string ListLevel = "listLevel";

    /// <summary><c>bool</c> — the range is flagged as a spelling error.</summary>
    public const string SpellingError = "spellingError";

    /// <summary><c>bool</c> — the range is flagged as a grammar error.</summary>
    public const string GrammarError = "grammarError";

    /// <summary>Font point size as a <c>double</c>.</summary>
    public const string FontSize = "fontSize";

    /// <summary>Font family as a <c>string</c>.</summary>
    public const string FontName = "fontName";
}
