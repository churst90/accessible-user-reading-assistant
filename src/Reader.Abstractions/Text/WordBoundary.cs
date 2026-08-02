namespace Aura.Abstractions.Text;

/// <summary>
/// The single definition of where a word starts and ends.
/// </summary>
/// <remarks>
/// <para>
/// There used to be two: <c>StringTextSurface</c> split on whitespace, and
/// <c>KeyEchoService</c> split on <c>char.IsPunctuation</c>. They disagreed,
/// so review said "don't" and typing echo said "don" then "t" for the same
/// text. Two definitions of one concept is how a codebase starts needing a
/// third to reconcile them.
/// </para>
/// <para>
/// The distinction that matters is between a <em>separator</em> (what breaks
/// one word from the next, for navigation) and a <em>terminator</em> (what
/// tells live typing echo that the word just finished). They are not the same
/// set: typing <c>cat.</c> should announce "cat" at the full stop, but
/// navigating over <c>don't</c> must not stop in the middle of it.
/// </para>
/// </remarks>
public static class WordBoundary
{
    /// <summary>
    /// True where one word ends and the next begins. Whitespace only — a word
    /// is a run of non-whitespace, so contractions and hyphenated compounds
    /// stay whole.
    /// </summary>
    public static bool IsSeparator(char c) => char.IsWhiteSpace(c);

    /// <summary>
    /// True for characters that finish a word being typed, so live echo can
    /// announce it without waiting for a space.
    /// </summary>
    /// <remarks>
    /// Apostrophe, hyphen and underscore are deliberately excluded: they occur
    /// <em>inside</em> words far more often than between them, and treating
    /// them as terminators is what produced "don" / "t" and
    /// "well" / "known". Everything here is punctuation that reliably ends a
    /// word in ordinary prose.
    /// </remarks>
    public static bool IsTerminator(char c)
        => char.IsWhiteSpace(c)
        || c is '.' or ',' or ';' or ':' or '!' or '?'
             or '"' or '`'
             or ')' or ']' or '}'
             or '(' or '[' or '{'
             or '/' or '\\' or '|'
             or '<' or '>' or '=' or '+' or '*' or '&' or '^' or '%' or '$' or '#' or '@' or '~';
}
