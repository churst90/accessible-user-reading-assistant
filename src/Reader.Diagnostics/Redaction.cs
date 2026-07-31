namespace OpenReader.Diagnostics;

/// <summary>
/// Keeps user content out of the log file.
/// </summary>
/// <remarks>
/// <para>
/// A screen reader speaks everything its user reads: banking pages, medical
/// records, private messages, recovery codes, the contents of a password
/// manager once it is unlocked. Anything that reaches the speech path is, by
/// definition, sensitive.
/// </para>
/// <para>
/// Logs live at <c>%LocalAppData%\OpenReader\logs\</c>, survive reboots, and
/// are the first thing a user is asked to attach to a bug report.
/// <c>DESIGN_PRINCIPLES.md</c> promises "we don't see anything they don't
/// choose to send" — that promise is only true if spoken text never reaches
/// disk in the first place.
/// </para>
/// <para>
/// <b>Redaction is on by default</b> and has to be turned off deliberately.
/// The default for a privacy control is the safe one, even when that makes
/// debugging harder.
/// </para>
/// <para>
/// Note what this deliberately does <em>not</em> do: hash the content. A hash
/// looks like a safe correlation token, but a screen reader announces single
/// characters and short words constantly, and a four-character digest of
/// <c>"a"</c> is reversible by anyone with a keyboard. Length plus timestamp
/// is enough to correlate repeats in a log, and it leaks nothing.
/// </para>
/// </remarks>
public static class Redaction
{
    /// <summary>
    /// Whether spoken text is replaced before it is logged. Defaults to
    /// <c>true</c>. Set from <c>Diagnostics.RedactContent</c> in config, and
    /// intended for a developer diagnosing a specific reproduction on their
    /// own machine.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Render text for logging: the text itself when redaction is off,
    /// otherwise a description of its shape.
    /// </summary>
    public static string Text(string? value)
    {
        if (!Enabled)
        {
            return value ?? "(null)";
        }
        if (value is null)
        {
            return "(null)";
        }
        if (value.Length == 0)
        {
            return "(empty)";
        }
        return $"(redacted, {value.Length} chars)";
    }
}
