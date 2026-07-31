namespace OpenReader.Scripting;

/// <summary>
/// Host-side declaration of the plugin API surface this OpenReader build
/// implements. Module manifests declare an <c>apiVersion</c> they were built
/// against and the host refuses to load incompatible modules.
/// </summary>
/// <remarks>
/// <para>
/// Compatibility rule: a module is loadable iff its declared
/// <see cref="OpenReader.Abstractions.Plugins.AppModuleManifest.ApiVersion"/>
/// has the <em>same major</em>
/// as <see cref="CurrentApiVersion"/> and a minor &lt;= the host's minor.
/// In other words, the host can host older minors of the same major;
/// new majors are breaking and require a recompile against the new SDK.
/// </para>
/// <para>
/// Bumping rules:
/// <list type="bullet">
///   <item><description>Add a new member to <c>IAppContext</c> or a new
///   abstraction → minor bump.</description></item>
///   <item><description>Remove or change the signature of an existing member
///   → major bump.</description></item>
/// </list>
/// Treat this constant as a versioned contract; it ships in the SDK NuGet
/// and external authors target it explicitly.
/// </para>
/// </remarks>
public static class PluginApi
{
    /// <summary>The contract version this host implements.</summary>
    public static readonly Version CurrentApiVersion = new(1, 0);

    /// <summary>True if the host can load a module declaring <paramref name="moduleApiVersion"/>.</summary>
    public static bool IsCompatible(Version moduleApiVersion)
    {
        ArgumentNullException.ThrowIfNull(moduleApiVersion);
        if (moduleApiVersion.Major != CurrentApiVersion.Major)
        {
            return false;
        }
        return moduleApiVersion.Minor <= CurrentApiVersion.Minor;
    }
}
