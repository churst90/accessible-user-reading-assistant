using System.Globalization;
using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Speech;

namespace Aura.Transcripts;

/// <summary>
/// A parsed <c>.transcript</c> file: a scenario and what it should say.
/// </summary>
/// <remarks>
/// <para>
/// The format is plain text on purpose. The artifact that gets reviewed is the
/// <em>diff</em>, and a reviewer looking at a removed "blank" line sees the bug
/// immediately in a way no serialised object graph reads.
/// </para>
/// <para>
/// Directives, one per line, arguments separated by whitespace (quote anything
/// containing a space):
/// </para>
/// <code>
/// node   &lt;id&gt; &lt;Role&gt; ["name"] [value="..."] [level=2] [pos=4] [size=10] [states=Selected|Checked]
/// focus  &lt;id&gt;
/// select &lt;id&gt;
/// caret  "text"
/// value  &lt;id&gt; "text"
/// alert  &lt;id&gt;
/// tooltip &lt;id&gt;
/// say    "text"
/// wait   &lt;milliseconds&gt;
/// drain             let everything queued finish playing before the next step
/// expect            everything after this line is expected output, one per line
/// </code>
/// </remarks>
public sealed class TranscriptScript
{
    private readonly List<Action<HeadlessReader>> _steps = [];
    private readonly Dictionary<string, AccessibleNode> _nodes = new(StringComparer.Ordinal);

    /// <summary>The scenario's name, from its file name.</summary>
    public required string Name { get; init; }

    /// <summary>Path on disk, so a failure can say which file to edit.</summary>
    public required string Path { get; init; }

    /// <summary>What it should say, in order.</summary>
    public List<string> Expected { get; } = [];

    /// <inheritdoc />
    public override string ToString() => Name;

    /// <summary>Run the scenario and return what was said.</summary>
    public IReadOnlyList<string> Run()
    {
        using var reader = new HeadlessReader();
        foreach (var step in _steps)
        {
            step(reader);
        }
        reader.Drain();
        return reader.Spoken;
    }

    public static TranscriptScript Parse(string path)
    {
        var script = new TranscriptScript
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            Path = path,
        };

        var inExpect = false;
        var lineNumber = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (inExpect)
            {
                script.Expected.Add(line);
                continue;
            }

            var args = Tokenize(line);
            var directive = args[0].ToLowerInvariant();
            if (directive == "expect")
            {
                inExpect = true;
                continue;
            }

            try
            {
                script.Add(directive, args);
            }
            catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or KeyNotFoundException)
            {
                throw new FormatException($"{path}({lineNumber}): {line} — {ex.Message}", ex);
            }
        }

        return script;
    }

    private void Add(string directive, string[] args)
    {
        switch (directive)
        {
            case "node":
                _nodes[args[1]] = BuildNode(args);
                break;

            case "focus":
            {
                var node = _nodes[args[1]];
                _steps.Add(r => r.Focus(node));
                break;
            }

            case "select":
            {
                var node = _nodes[args[1]];
                _steps.Add(r => r.Event(SpeechReason.SelectionChanged, node));
                break;
            }

            case "value":
            {
                var node = _nodes[args[1]];
                var text = args[2];
                _steps.Add(r => r.Event(SpeechReason.ValueChanged, node, text));
                break;
            }

            case "caret":
            {
                var text = args[1];
                _steps.Add(r => r.Event(SpeechReason.CaretMoved, null, text));
                break;
            }

            // Takes a node, not a string: a real UIA alert names an element,
            // and the announcement comes from that element. Passing bare text
            // here would test a path the provider never produces.
            case "tooltip":
            {
                var node = _nodes[args[1]];
                _steps.Add(r => r.Event(SpeechReason.ToolTipOpened, node));
                break;
            }

            case "alert":
            {
                var node = _nodes[args[1]];
                _steps.Add(r => r.Event(SpeechReason.AlertRaised, node));
                break;
            }

            case "say":
            {
                var text = args[1];
                _steps.Add(r => r.Event(SpeechReason.UserAnnouncement, null, text));
                break;
            }

            // Without this, a whole script queues before anything is spoken, so
            // a second focus event cancel-groups the first away and the
            // scenario silently tests only its last step. "drain" is the user
            // pausing long enough to hear what they asked for.
            case "drain":
                _steps.Add(r => r.Drain());
                break;

            case "wait":
            {
                var ms = int.Parse(args[1], CultureInfo.InvariantCulture);
                _steps.Add(r => r.Wait(TimeSpan.FromMilliseconds(ms)));
                break;
            }

            default:
                throw new FormatException($"unknown directive '{directive}'");
        }
    }

    private static AccessibleNode BuildNode(string[] args)
    {
        var id = args[1];
        var role = Enum.Parse<AccessibleRole>(args[2], ignoreCase: true);
        string? name = null;
        string? value = null;
        var states = AccessibleStates.None;
        var extras = new Dictionary<string, object?>(StringComparer.Ordinal);

        for (var i = 3; i < args.Length; i++)
        {
            var arg = args[i];
            var eq = arg.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                name = arg;
                continue;
            }
            var key = arg[..eq];
            var val = arg[(eq + 1)..];
            switch (key.ToLowerInvariant())
            {
                case "value": value = val; break;
                case "level": extras["uia.Level"] = int.Parse(val, CultureInfo.InvariantCulture); break;
                case "pos": extras["uia.PositionInSet"] = int.Parse(val, CultureInfo.InvariantCulture); break;
                case "size": extras["uia.SizeOfSet"] = int.Parse(val, CultureInfo.InvariantCulture); break;
                case "states":
                    foreach (var s in val.Split('|', StringSplitOptions.RemoveEmptyEntries))
                    {
                        states |= Enum.Parse<AccessibleStates>(s, ignoreCase: true);
                    }
                    break;
                default: throw new FormatException($"unknown node attribute '{key}'");
            }
        }

        return new AccessibleNode(new NodeId(id), role, name, value, null, states, null, () => [], extras);
    }

    /// <summary>Split on whitespace, keeping quoted runs together.</summary>
    private static string[] Tokenize(string line)
    {
        var result = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i]))
            {
                i++;
            }
            if (i >= line.Length)
            {
                break;
            }
            if (line[i] == '"')
            {
                var end = line.IndexOf('"', i + 1);
                if (end < 0)
                {
                    throw new FormatException("unterminated quote");
                }
                result.Add(line[(i + 1)..end]);
                i = end + 1;
            }
            else
            {
                var start = i;
                while (i < line.Length && !char.IsWhiteSpace(line[i]))
                {
                    i++;
                }
                result.Add(line[start..i]);
            }
        }
        return [.. result];
    }
}
