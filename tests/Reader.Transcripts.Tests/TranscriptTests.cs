using System.Reflection;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Aura.Transcripts;

/// <summary>
/// Runs every scenario in <c>Scenarios/</c> and diffs what was said against
/// what the file says should be said.
/// </summary>
/// <remarks>
/// <para>
/// This is the regression net. Every announcement bug found on hardware becomes
/// one file here, and from that point it cannot come back silently. Nothing
/// else in this project has that property, and no other screen reader has it at
/// all — which is why it ranks above every feature on the roadmap.
/// </para>
/// <para>
/// To accept new output after a deliberate behaviour change:
/// </para>
/// <code>
/// AURA_UPDATE_TRANSCRIPTS=1 dotnet test tests/Reader.Transcripts.Tests
/// </code>
/// <para>
/// Then read the diff. The diff <em>is</em> the review: a removed line is a
/// thing the user stopped hearing, and that is exactly the change that has
/// slipped through five times.
/// </para>
/// </remarks>
public class TranscriptTests
{
    public static TheoryData<TranscriptScript> Scenarios()
    {
        var data = new TheoryData<TranscriptScript>();
        foreach (var path in Directory.EnumerateFiles(ScenarioDirectory(), "*.transcript").Order(StringComparer.Ordinal))
        {
            data.Add(TranscriptScript.Parse(path));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Scenario(TranscriptScript script)
    {
        var actual = script.Run();

        if (ShouldUpdate)
        {
            Rewrite(script, actual);
            return;
        }

        actual.Should().Equal(script.Expected,
            $"the scenario in {Path.GetFileName(script.Path)} says so — "
            + "if this change is intended, rerun with AURA_UPDATE_TRANSCRIPTS=1 and review the diff");
    }

    [Fact]
    public void There_are_scenarios_to_run()
        => Directory.EnumerateFiles(ScenarioDirectory(), "*.transcript").Should().NotBeEmpty(
            "an empty transcript suite passes without testing anything, which is worse than failing");

    private static bool ShouldUpdate =>
        Environment.GetEnvironmentVariable("AURA_UPDATE_TRANSCRIPTS") is "1" or "true";

    /// <summary>
    /// Rewrite the expectations in place, in the source tree rather than the
    /// build output, so the diff is reviewable.
    /// </summary>
    private static void Rewrite(TranscriptScript script, IReadOnlyList<string> actual)
    {
        var source = SourcePathFor(script);
        var lines = File.ReadAllLines(source);
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            sb.AppendLine(line);
            if (line.Trim().Equals("expect", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }
        foreach (var said in actual)
        {
            sb.Append("  ").AppendLine(said);
        }
        File.WriteAllText(source, sb.ToString());
    }

    private static string ScenarioDirectory()
        => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Scenarios");

    private static string SourcePathFor(TranscriptScript script)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AURA.slnx")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            return script.Path;
        }
        var candidate = Path.Combine(dir.FullName, "tests", "Reader.Transcripts.Tests", "Scenarios",
            Path.GetFileName(script.Path));
        return File.Exists(candidate) ? candidate : script.Path;
    }
}
