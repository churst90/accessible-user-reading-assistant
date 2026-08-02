using System.Text.Json;
using System.Text.Json.Serialization;
using Aura.Abstractions.Plugins;

namespace Aura.Scripting;

/// <summary>
/// On-disk schema for a plugin's <c>manifest.json</c>. Deserialized by the
/// host before the plugin's assembly is loaded so we can refuse incompatible
/// modules without instantiating their types.
/// </summary>
/// <remarks>
/// The manifest file is the source of truth for the host's compatibility
/// gate. The <see cref="IAppModule"/> instance also exposes a manifest, but
/// we treat that as an informational mirror — the host trusts the file.
/// </remarks>
public sealed record PluginManifestFile
{
    /// <summary>Reverse-DNS module identifier.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Module version. Semantic-version-shaped string.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "0.0.0";

    /// <summary>Plugin API version this module targets (e.g. <c>"1.0"</c>).</summary>
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; init; } = "1.0";

    /// <summary>Filename of the assembly to load, relative to the plugin folder.</summary>
    [JsonPropertyName("assembly")]
    public string Assembly { get; init; } = string.Empty;

    /// <summary>Full type name of the <see cref="IAppModule"/> implementation.</summary>
    [JsonPropertyName("moduleType")]
    public string ModuleType { get; init; } = string.Empty;

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Capabilities the plugin needs from the host. Examples (open set):
    /// <list type="bullet">
    ///   <item><c>accessibility-read</c> — observe focus / value / text events.</item>
    ///   <item><c>accessibility-write</c> — invoke patterns (Click, Toggle, etc.).</item>
    ///   <item><c>speech-rules</c> — register <see cref="Aura.Abstractions.Speech.SpeechRule"/>s.</item>
    ///   <item><c>commands</c> — register reader commands and chord bindings.</item>
    ///   <item><c>audio-output</c> — play earcons / audio-theme cues.</item>
    ///   <item><c>settings-panel</c> — contribute a Settings dialog category.</item>
    ///   <item><c>network-out:&lt;host&gt;:&lt;port&gt;</c> — outbound network access (relay, telemetry).</item>
    ///   <item><c>filesystem:&lt;path&gt;</c> — read/write outside the plugin's own folder.</item>
    ///   <item><c>process-launch</c> — start external processes.</item>
    /// </list>
    /// <para>
    /// Today this field is <em>declarative only</em> — the host logs the
    /// declared set on load. Phase 4d will tighten this into a host-enforced
    /// gate with a UI prompt the first time a sensitive capability is
    /// exercised. Plugins should declare what they need now so manifests are
    /// forward-compatible without a re-publish.
    /// </para>
    /// </summary>
    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string>? Capabilities { get; init; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parse from JSON text. Throws <see cref="JsonException"/> on malformed input.</summary>
    public static PluginManifestFile FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<PluginManifestFile>(json, Options)
            ?? throw new JsonException("plugin manifest deserialized to null");
    }

    /// <summary>
    /// Read and validate the manifest at <paramref name="path"/>. Returns
    /// <c>null</c> with a reason if the file is missing or malformed.
    /// </summary>
    public static PluginManifestFile? TryLoad(string path, out string? error)
    {
        error = null;
        try
        {
            var text = File.ReadAllText(path);
            var manifest = FromJson(text);
            if (string.IsNullOrWhiteSpace(manifest.Id)
                || string.IsNullOrWhiteSpace(manifest.Assembly)
                || string.IsNullOrWhiteSpace(manifest.ModuleType))
            {
                error = "manifest is missing one of: id, assembly, moduleType";
                return null;
            }
            if (!System.Version.TryParse(manifest.ApiVersion, out _)
                || !System.Version.TryParse(manifest.Version, out _))
            {
                error = "manifest version or apiVersion is not a valid Version string";
                return null;
            }
            return manifest;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Project to the strongly-typed <see cref="AppModuleManifest"/> the abstractions expect.</summary>
    public AppModuleManifest ToManifest() => new(
        Id: Id,
        DisplayName: string.IsNullOrEmpty(DisplayName) ? Id : DisplayName,
        Version: System.Version.Parse(Version),
        ApiVersion: System.Version.Parse(ApiVersion),
        Author: Author,
        Description: Description);
}
