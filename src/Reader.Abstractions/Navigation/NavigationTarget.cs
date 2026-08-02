namespace OpenReader.Abstractions.Navigation;

/// <summary>
/// A kind of element that quick navigation can jump between in
/// <see cref="ReaderMode.Read"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each of these maps to a single-letter key in Read mode, following the
/// conventions users already have in their fingers from NVDA and JAWS —
/// <c>h</c> heading, <c>k</c> link, <c>b</c> button, <c>f</c> form field,
/// <c>t</c> table, <c>l</c> list, <c>d</c> landmark. Shift reverses direction.
/// Deviating from those bindings for their own sake would cost every switching
/// user their muscle memory and buy nothing.
/// </para>
/// <para>
/// Deliberately an enum and not an open string set, unlike
/// <see cref="Text.TextAttributes"/>: these bind to keys and appear in the
/// rebinding UI, so the host has to be able to enumerate them. A plugin that
/// wants a new jump target is asking for a new command, which is what
/// <c>IPluginCommand</c> is for.
/// </para>
/// </remarks>
public enum NavigationTarget
{
    /// <summary>Any heading, regardless of depth.</summary>
    Heading,

    /// <summary>A heading at a specific depth; pair with <c>level</c> on the query.</summary>
    HeadingAtLevel,

    /// <summary>A hyperlink, visited or not.</summary>
    Link,

    /// <summary>A link the user has not followed.</summary>
    UnvisitedLink,

    Button,

    /// <summary>Any focusable form control — edit, combo, checkbox, radio.</summary>
    FormField,

    /// <summary>An editable text field specifically.</summary>
    EditField,

    CheckBox,

    RadioButton,

    ComboBox,

    /// <summary>A list container, not its items.</summary>
    List,

    ListItem,

    Table,

    /// <summary>An ARIA landmark or its platform equivalent.</summary>
    Landmark,

    /// <summary>An image or other non-text graphic.</summary>
    Graphic,

    /// <summary>A block quotation.</summary>
    BlockQuote,

    /// <summary>A run of preformatted or monospaced text.</summary>
    CodeBlock,

    /// <summary>A separator or horizontal rule.</summary>
    Separator,

    /// <summary>
    /// A frame or embedded document. Worth its own target because embedded
    /// content is where users most often get lost.
    /// </summary>
    Frame,
}
