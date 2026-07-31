using OpenReader.Abstractions.Text;

namespace OpenReader.Core.Text;

/// <summary>What kind of caret movement happened between two observations.</summary>
public enum CaretMotionKind
{
    /// <summary>Nothing to announce — the caret did not move.</summary>
    None,

    /// <summary>Moved within a line by exactly one character.</summary>
    Character,

    /// <summary>Moved within a line by more than one character.</summary>
    Word,

    /// <summary>Crossed a line boundary.</summary>
    Line,

    /// <summary>The selection got bigger. <see cref="CaretMotion.Text"/> is the text that joined it.</summary>
    SelectionGrew,

    /// <summary>The selection got smaller. <see cref="CaretMotion.Text"/> is the text that left it.</summary>
    SelectionShrank,

    /// <summary>A selection was dropped entirely. <see cref="CaretMotion.Text"/> is what had been selected.</summary>
    SelectionCleared,
}

/// <summary>
/// The announcement implied by a caret movement: what kind of move it was and
/// the text to speak.
/// </summary>
/// <remarks>
/// <see cref="Text"/> may legitimately be empty — the caret sitting past the
/// last character of a line, or on a blank line. The speech layer decides
/// whether that becomes "blank", "end of line", or silence; that is a
/// presentation choice and does not belong here.
/// </remarks>
public sealed record CaretMotion(CaretMotionKind Kind, string Text)
{
    /// <summary>Nothing happened.</summary>
    public static readonly CaretMotion None = new(CaretMotionKind.None, string.Empty);

    /// <summary>The granularity to speak at, or <c>null</c> for selection changes.</summary>
    public TextUnit? Unit => Kind switch
    {
        CaretMotionKind.Character => TextUnit.Character,
        CaretMotionKind.Word => TextUnit.Word,
        CaretMotionKind.Line => TextUnit.Line,
        _ => null,
    };
}
