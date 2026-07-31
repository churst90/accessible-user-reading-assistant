using System.Globalization;
using OpenReader.Abstractions.Accessibility;
using OpenReader.Abstractions.Speech;
using YamlDotNet.RepresentationModel;

namespace OpenReader.Speech.Rules;

/// <summary>
/// Loads <see cref="SpeechRule"/>s from YAML.
/// </summary>
/// <remarks>
/// The format is documented in <c>docs/SPEECH_PIPELINE.md</c>. This loader walks
/// the YAML representation model directly rather than deserializing to typed
/// objects, which keeps it AOT-friendly (no reflection-based deserialization).
/// </remarks>
public static class YamlRuleLoader
{
    private const string DefaultsResourceName = "OpenReader.Speech.Rules.defaults.yaml";

    /// <summary>Load the rules embedded in the Reader.Speech assembly.</summary>
    public static IReadOnlyList<SpeechRule> LoadDefaults()
    {
        using var stream = typeof(YamlRuleLoader).Assembly.GetManifestResourceStream(DefaultsResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{DefaultsResourceName}' not found. " +
                $"Available: {string.Join(", ", typeof(YamlRuleLoader).Assembly.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        return Load(reader);
    }

    /// <summary>Load rules from a YAML file on disk.</summary>
    public static IReadOnlyList<SpeechRule> LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var reader = new StreamReader(path);
        return Load(reader);
    }

    /// <summary>Load rules from a string.</summary>
    public static IReadOnlyList<SpeechRule> LoadFromString(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        using var reader = new StringReader(yaml);
        return Load(reader);
    }

    /// <summary>Load rules from a TextReader.</summary>
    public static IReadOnlyList<SpeechRule> Load(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var stream = new YamlStream();
        stream.Load(reader);
        if (stream.Documents.Count == 0)
        {
            return Array.Empty<SpeechRule>();
        }

        var root = stream.Documents[0].RootNode as YamlMappingNode
            ?? throw new InvalidDataException("Top-level YAML node must be a mapping.");

        if (root.Children.TryGetValue(new YamlScalarNode("version"), out var versionNode))
        {
            var version = ScalarText(versionNode);
            if (!string.Equals(version, "1", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported speech-rules version '{version}'. Expected '1'.");
            }
        }

        if (!root.Children.TryGetValue(new YamlScalarNode("rules"), out var rulesNode)
            || rulesNode is not YamlSequenceNode rulesSeq)
        {
            return Array.Empty<SpeechRule>();
        }

        var result = new List<SpeechRule>(rulesSeq.Children.Count);
        var index = 0;
        foreach (var item in rulesSeq.Children)
        {
            if (item is not YamlMappingNode ruleMap)
            {
                throw new InvalidDataException($"rules[{index}] must be a mapping.");
            }
            result.Add(ParseRule(ruleMap, index));
            index++;
        }

        return result;
    }

    private static SpeechRule ParseRule(YamlMappingNode node, int index)
    {
        var id = RequireScalar(node, "id", $"rules[{index}].id");
        var priority = node.Children.TryGetValue(new YamlScalarNode("priority"), out var p)
            ? ParseInt(ScalarText(p), $"rules[{index}].priority")
            : 0;

        var scope = node.Children.TryGetValue(new YamlScalarNode("scope"), out var scopeNode) && scopeNode is YamlMappingNode scopeMap
            ? ParseScope(scopeMap, $"rules[{index}].scope")
            : new SpeechRuleScope();

        if (!node.Children.TryGetValue(new YamlScalarNode("action"), out var actionNode) || actionNode is not YamlMappingNode actionMap)
        {
            throw new InvalidDataException($"rules[{index}] (id='{id}') is missing an action mapping.");
        }
        var action = ParseAction(actionMap, $"rules[{index}].action (id='{id}')");

        return new SpeechRule(id, priority, scope, action);
    }

    private static SpeechRuleScope ParseScope(YamlMappingNode node, string path)
    {
        AccessibleRole? role = null;
        SpeechReason? reason = null;
        var required = AccessibleStates.None;
        var forbidden = AccessibleStates.None;
        string? app = null;
        string? textRegex = null;

        foreach (var entry in node.Children)
        {
            var key = ScalarText(entry.Key);
            switch (key)
            {
                case "role":
                    role = ParseEnum<AccessibleRole>(ScalarText(entry.Value), $"{path}.role");
                    break;
                case "reason":
                    reason = ParseEnum<SpeechReason>(ScalarText(entry.Value), $"{path}.reason");
                    break;
                case "requiredStates":
                    required = ParseStates(entry.Value, $"{path}.requiredStates");
                    break;
                case "forbiddenStates":
                    forbidden = ParseStates(entry.Value, $"{path}.forbiddenStates");
                    break;
                case "app":
                    app = ScalarText(entry.Value);
                    break;
                case "textRegex":
                    textRegex = ScalarText(entry.Value);
                    break;
                default:
                    throw new InvalidDataException($"{path}: unknown scope key '{key}'.");
            }
        }

        return new SpeechRuleScope(role, required, forbidden, reason, app, textRegex);
    }

    private static SpeechRuleAction ParseAction(YamlMappingNode node, string path)
    {
        var type = RequireScalar(node, "type", $"{path}.type");
        switch (type)
        {
            case "emit":
                return new SpeechRuleAction.Emit(RequireScalar(node, "template", $"{path}.template"));

            case "rewrite":
                return new SpeechRuleAction.Rewrite(
                    Pattern: RequireScalar(node, "pattern", $"{path}.pattern"),
                    Replacement: RequireScalar(node, "replacement", $"{path}.replacement"));

            case "modulate":
                if (!node.Children.TryGetValue(new YamlScalarNode("prosody"), out var prosodyNode) || prosodyNode is not YamlMappingNode prosodyMap)
                {
                    throw new InvalidDataException($"{path}: 'modulate' requires a 'prosody' mapping.");
                }
                return new SpeechRuleAction.Modulate(ParseProsody(prosodyMap, $"{path}.prosody"));

            case "setVoice":
                return new SpeechRuleAction.SetVoice(RequireScalar(node, "voiceId", $"{path}.voiceId"));

            case "suppress":
                return new SpeechRuleAction.Suppress();

            default:
                throw new InvalidDataException($"{path}: unknown action type '{type}'. Expected one of emit, rewrite, modulate, setVoice, suppress.");
        }
    }

    private static ProsodyHint ParseProsody(YamlMappingNode node, string path)
    {
        var pitch = 0f;
        var rate = 100f;
        var volume = 0f;

        foreach (var entry in node.Children)
        {
            var key = ScalarText(entry.Key);
            var value = ParseFloat(ScalarText(entry.Value), $"{path}.{key}");
            switch (key)
            {
                case "pitchDelta":
                case "pitch":
                    pitch = value;
                    break;
                case "ratePercent":
                case "rate":
                    rate = value;
                    break;
                case "volumeDelta":
                case "volume":
                    volume = value;
                    break;
                default:
                    throw new InvalidDataException($"{path}: unknown prosody key '{key}'.");
            }
        }

        return new ProsodyHint(pitch, rate, volume);
    }

    private static readonly char[] StateSeparators = { ',', ' ', '|' };

    private static AccessibleStates ParseStates(YamlNode node, string path)
    {
        var result = AccessibleStates.None;

        switch (node)
        {
            case YamlScalarNode scalar:
                {
                    var raw = scalar.Value ?? string.Empty;
                    foreach (var part in raw.Split(StateSeparators, StringSplitOptions.RemoveEmptyEntries))
                    {
                        result |= ParseEnum<AccessibleStates>(part, path);
                    }
                    return result;
                }

            case YamlSequenceNode seq:
                foreach (var item in seq.Children)
                {
                    result |= ParseEnum<AccessibleStates>(ScalarText(item), path);
                }
                return result;

            default:
                throw new InvalidDataException($"{path}: states must be a scalar or sequence.");
        }
    }

    private static string RequireScalar(YamlMappingNode node, string key, string path)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var value))
        {
            throw new InvalidDataException($"{path}: required key missing.");
        }
        return ScalarText(value);
    }

    private static string ScalarText(YamlNode node)
    {
        if (node is YamlScalarNode s)
        {
            return s.Value ?? string.Empty;
        }
        throw new InvalidDataException($"Expected scalar but got {node.GetType().Name}.");
    }

    private static int ParseInt(string text, string path)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidDataException($"{path}: '{text}' is not a valid integer.");
        }
        return result;
    }

    private static float ParseFloat(string text, string path)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidDataException($"{path}: '{text}' is not a valid number.");
        }
        return result;
    }

    private static T ParseEnum<T>(string text, string path) where T : struct, Enum
    {
        if (!Enum.TryParse<T>(text, ignoreCase: true, out var result))
        {
            throw new InvalidDataException($"{path}: '{text}' is not a valid {typeof(T).Name}.");
        }
        return result;
    }
}
