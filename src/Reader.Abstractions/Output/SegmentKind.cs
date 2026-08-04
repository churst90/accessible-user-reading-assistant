namespace Aura.Abstractions.Output;

/// <summary>
/// What a <see cref="PresentationSegment"/> is, so that verbosity is a property
/// of the system rather than of every rule that composes an announcement.
/// </summary>
/// <remarks>
/// <para>
/// "Never announce hints", "announce position only in lists", "show state in
/// braille but do not speak it" are all filters over kinds. In NVDA the same
/// decisions are spread through <c>speech.py</c> as a configuration check at
/// every call site, which is why they cannot be changed without editing code.
/// Here they are one filter over a list.
/// </para>
/// </remarks>
public enum SegmentKind
{
    /// <summary>
    /// Literal words from a rule template that carry no declared kind — "check
    /// box", "read only". Faithful, and deliberately unfiltered: a template can
    /// declare a kind explicitly (<c>{state:checked}</c>) when the distinction
    /// starts to matter.
    /// </summary>
    Literal = 0,

    /// <summary>The text itself — a line, a word, a character, a document.</summary>
    Content,

    /// <summary>The control's accessible name.</summary>
    Name,

    /// <summary>The control's type, as a word: "button", "list item".</summary>
    Role,

    /// <summary>The control's value.</summary>
    Value,

    /// <summary>The control's description or help text.</summary>
    Description,

    /// <summary>"checked", "expanded", "read only".</summary>
    State,

    /// <summary>"3 of 10", "level 2".</summary>
    Position,

    /// <summary>Entering or leaving a container — "list", "out of list".</summary>
    Structure,

    /// <summary>Leading whitespace, rendered as words or as a tone.</summary>
    Indent,

    /// <summary>
    /// An earcon. <see cref="PresentationSegment.Text"/> is a cue id, not
    /// words, and a renderer that cannot play it emits nothing rather than
    /// speaking the id.
    /// </summary>
    Cue,

    /// <summary>"press space to activate" — suppressible as a class.</summary>
    Hint,
}
