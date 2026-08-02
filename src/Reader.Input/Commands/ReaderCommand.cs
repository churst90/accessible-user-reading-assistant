namespace Aura.Input.Commands;

/// <summary>
/// High-level reader actions emitted by the gesture layer and consumed by
/// command handlers. Names match the user-visible mental model — <c>ReadLine</c>
/// rather than <c>HandleNumPad8</c>.
/// </summary>
public enum ReaderCommand
{
    None = 0,

    StopSpeech,
    SayAll,

    ReadCharacter,
    ReadNextCharacter,
    ReadPreviousCharacter,

    ReadWord,
    ReadNextWord,
    ReadPreviousWord,

    ReadLine,
    ReadNextLine,
    ReadPreviousLine,

    ReviewMoveToFocus,
    ReviewMoveToTop,
    ReviewMoveToBottom,

    ReportFocus,
    ReportTitle,
    ReportTime,
    ReportDate,

    SayAllFromCursor,
    CyclePunctuationLevel,
    ToggleKeyboardHelp,
    ToggleEnabled,

    OpenSettings,
    OpenDocumentation,
    OpenExitDialog,
    OpenSynthesizerDialog,

    /// <summary>
    /// Copy a diagnostic snapshot to the clipboard and say where the log is.
    /// Exists so a bug report can contain facts instead of "it stopped
    /// working" — the reporter is, by definition, unable to read the screen
    /// to gather them.
    /// </summary>
    ReportDiagnostics,

    /// <summary>
    /// Switch between Read mode and Type mode. Bound to Reader+Space, matching
    /// NVDA's browse/focus toggle so switching users keep the reflex.
    /// </summary>
    ToggleReaderMode,
}

/// <summary>Human-readable labels for <see cref="ReaderCommand"/>s. Centralized so the help-mode announcer and the rebind UI agree.</summary>
public static class ReaderCommandLabels
{
    /// <summary>A short user-facing label for <paramref name="command"/>.</summary>
    public static string Humanize(ReaderCommand command) => command switch
    {
        ReaderCommand.StopSpeech => "stop speech",
        ReaderCommand.SayAll => "say all",
        ReaderCommand.SayAllFromCursor => "say all from cursor",
        ReaderCommand.ReadCharacter => "read character",
        ReaderCommand.ReadNextCharacter => "next character",
        ReaderCommand.ReadPreviousCharacter => "previous character",
        ReaderCommand.ReadWord => "read word",
        ReaderCommand.ReadNextWord => "next word",
        ReaderCommand.ReadPreviousWord => "previous word",
        ReaderCommand.ReadLine => "read line",
        ReaderCommand.ReadNextLine => "next line",
        ReaderCommand.ReadPreviousLine => "previous line",
        ReaderCommand.ReviewMoveToFocus => "review move to focus",
        ReaderCommand.ReviewMoveToTop => "review move to top",
        ReaderCommand.ReviewMoveToBottom => "review move to bottom",
        ReaderCommand.ReportFocus => "report focus",
        ReaderCommand.ReportTitle => "report title",
        ReaderCommand.ReportTime => "report time",
        ReaderCommand.ReportDate => "report date",
        ReaderCommand.CyclePunctuationLevel => "cycle punctuation",
        ReaderCommand.ToggleKeyboardHelp => "toggle keyboard help",
        ReaderCommand.ToggleEnabled => "toggle screen reader",
        ReaderCommand.OpenSettings => "open settings",
        ReaderCommand.OpenDocumentation => "open documentation",
        ReaderCommand.OpenExitDialog => "exit",
        ReaderCommand.OpenSynthesizerDialog => "open synthesizer",
        ReaderCommand.ReportDiagnostics => "copy diagnostics",
        ReaderCommand.ToggleReaderMode => "toggle read and type mode",
        _ => command.ToString(),
    };
}
