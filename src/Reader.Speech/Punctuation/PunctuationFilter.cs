using System.Text;

namespace OpenReader.Speech.Punctuation;

/// <summary>
/// Applies a <see cref="PunctuationLevel"/> to a string to produce the form
/// that should actually be spoken.
/// </summary>
/// <remarks>
/// This is intentionally simple: a single-pass char loop with a level-keyed
/// classifier. Localized symbol names (e.g. "exclamation point" vs "bang")
/// belong in a future symbols dictionary loaded from YAML.
/// </remarks>
public static class PunctuationFilter
{
    public static string Apply(string text, PunctuationLevel level)
    {
        if (string.IsNullOrEmpty(text) || level == PunctuationLevel.None && !ContainsPunctuation(text))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 16);
        foreach (var ch in text)
        {
            var verbalize = ShouldVerbalize(ch, level);
            if (verbalize is null)
            {
                sb.Append(ch);
                continue;
            }
            if (verbalize.Length == 0)
            {
                // Strip the character; replace with a space to preserve word boundaries.
                if (sb.Length > 0 && sb[^1] != ' ')
                {
                    sb.Append(' ');
                }
                continue;
            }
            if (sb.Length > 0 && sb[^1] != ' ')
            {
                sb.Append(' ');
            }
            sb.Append(verbalize);
            sb.Append(' ');
        }
        return CollapseWhitespace(sb.ToString().Trim());
    }

    private static string CollapseWhitespace(string s)
    {
        if (s.Length == 0)
        {
            return s;
        }
        var sb = new StringBuilder(s.Length);
        var lastWasSpace = false;
        foreach (var ch in s)
        {
            if (ch == ' ')
            {
                if (lastWasSpace)
                {
                    continue;
                }
                lastWasSpace = true;
                sb.Append(' ');
            }
            else
            {
                lastWasSpace = false;
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns the spoken form of <paramref name="ch"/>, the empty string to
    /// strip the character, or <c>null</c> to leave it as-is in the text.
    /// </summary>
    private static string? ShouldVerbalize(char ch, PunctuationLevel level)
    {
        // Letters / digits / whitespace always pass through verbatim.
        if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
        {
            return null;
        }
        if (!(char.IsPunctuation(ch) || char.IsSymbol(ch)))
        {
            return null;
        }

        return level switch
        {
            PunctuationLevel.None => string.Empty,
            PunctuationLevel.Some => IsSentenceTerminator(ch) ? null : string.Empty,
            PunctuationLevel.Most => IsSentenceTerminator(ch) ? null : NameOf(ch),
            PunctuationLevel.All => NameOf(ch),
            _ => null,
        };
    }

    /// <summary>
    /// The spoken name of a single punctuation / symbol / whitespace character
    /// ("," → "comma", " " → "space"), or <c>null</c> if it has no special
    /// name (letters, digits). Used for character-by-character navigation,
    /// where the user moved ONTO the character deliberately and must hear it
    /// regardless of the active <see cref="PunctuationLevel"/> — otherwise
    /// arrowing across a line silently skips its punctuation.
    /// </summary>
    public static string? SpokenName(char ch)
    {
        if (ch == ' ')
        {
            return "space";
        }
        if (ch == '\t')
        {
            return "tab";
        }
        if (!(char.IsPunctuation(ch) || char.IsSymbol(ch)))
        {
            return null;
        }
        return NameOf(ch);
    }

    private static bool IsSentenceTerminator(char ch) =>
        ch is '.' or ',' or ';' or ':' or '?' or '!' or '"' or '\'';

    private static bool ContainsPunctuation(string text)
    {
        foreach (var ch in text)
        {
            if (char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                return true;
            }
        }
        return false;
    }

    private static string NameOf(char ch) => ch switch
    {
        '.' => "dot",
        ',' => "comma",
        ';' => "semicolon",
        ':' => "colon",
        '?' => "question",
        '!' => "exclamation",
        '"' => "quote",
        '\'' => "apostrophe",
        '`' => "backtick",
        '(' => "left paren",
        ')' => "right paren",
        '[' => "left bracket",
        ']' => "right bracket",
        '{' => "left brace",
        '}' => "right brace",
        '<' => "less",
        '>' => "greater",
        '/' => "slash",
        '\\' => "backslash",
        '|' => "pipe",
        '-' => "dash",
        '_' => "underscore",
        '+' => "plus",
        '=' => "equals",
        '*' => "star",
        '&' => "ampersand",
        '^' => "caret",
        '%' => "percent",
        '$' => "dollar",
        '#' => "number",
        '@' => "at",
        '~' => "tilde",
        _ => ch.ToString(),
    };
}
