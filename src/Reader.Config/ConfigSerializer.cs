using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenReader.Config;

/// <summary>
/// JSON serialization for <see cref="ReaderConfig"/>. Pretty-printed,
/// camelCase, missing-property tolerant.
/// </summary>
internal static class ConfigSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ReaderConfig? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        return JsonSerializer.Deserialize<ReaderConfig>(json, Options);
    }

    public static string Serialize(ReaderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return JsonSerializer.Serialize(config, Options);
    }
}
