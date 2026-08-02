namespace Aura.Abstractions.Plugins;

/// <summary>
/// Static metadata declared by an <see cref="IAppModule"/>. The host validates
/// the API version before loading; mismatched modules are refused with a clear
/// log line.
/// </summary>
/// <param name="Id">Reverse-DNS module identifier (e.g. <c>com.example.outlook-shim</c>).</param>
/// <param name="DisplayName">Human-friendly name for UI surfaces.</param>
/// <param name="Version">Module version (semantic).</param>
/// <param name="ApiVersion">
/// The Aura plugin API version this module was built against. The host
/// loads modules whose major matches and whose minor is &lt;= host API minor.
/// </param>
/// <param name="Author">Optional author / organization.</param>
/// <param name="Description">Optional one-line description for the plugin manager UI.</param>
public sealed record AppModuleManifest(
    string Id,
    string DisplayName,
    Version Version,
    Version ApiVersion,
    string? Author = null,
    string? Description = null);
