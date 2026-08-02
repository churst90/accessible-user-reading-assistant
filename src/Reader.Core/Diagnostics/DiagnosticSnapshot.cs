using System.Globalization;
using System.Text;

namespace Aura.Core.Diagnostics;

/// <summary>
/// Builds the text of a bug report.
/// </summary>
/// <remarks>
/// <para>
/// A screen reader's users cannot read the screen to tell you what is on it.
/// Without something like this, a report is "it stopped talking", and the
/// round-trip to establish version, synthesiser, and which application was
/// focused costs both sides a day.
/// </para>
/// <para>
/// Content is deliberately factual and non-identifying: versions, settings,
/// the executable name of the focused application. <b>No spoken text and no
/// control values</b> — the same reasoning as <c>Redaction</c>. What the user
/// was reading is not diagnostic information, and a bug report gets pasted
/// into public issue trackers.
/// </para>
/// </remarks>
public static class DiagnosticSnapshot
{
    /// <summary>A field to include in the report.</summary>
    public readonly record struct Field(string Name, string? Value);

    /// <summary>
    /// Render fields as a report. Null and empty values become
    /// <c>(unknown)</c> rather than being dropped: "we could not determine the
    /// synthesiser" is itself a useful symptom.
    /// </summary>
    public static string Build(string title, IEnumerable<Field> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var sb = new StringBuilder(512);
        sb.Append("=== ").Append(title).AppendLine(" ===");
        foreach (var field in fields)
        {
            sb.Append(field.Name)
              .Append(": ")
              .AppendLine(string.IsNullOrWhiteSpace(field.Value) ? "(unknown)" : field.Value);
        }
        sb.AppendLine();
        sb.AppendLine("No spoken text or control content is included by design.");
        return sb.ToString();
    }

    /// <summary>
    /// The spoken confirmation. Short on purpose — the detail is on the
    /// clipboard, and reading a wall of version numbers aloud helps nobody.
    /// </summary>
    public static string SpokenSummary(int fieldCount)
        => string.Create(CultureInfo.InvariantCulture,
            $"Diagnostics copied to clipboard, {fieldCount} items.");
}
