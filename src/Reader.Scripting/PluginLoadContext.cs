using System.Reflection;
using System.Runtime.Loader;

namespace OpenReader.Scripting;

/// <summary>
/// Collectible <see cref="AssemblyLoadContext"/> that loads a plugin's
/// assemblies isolated from the host's default load context.
/// </summary>
/// <remarks>
/// <para>
/// The contract types — anything in <c>OpenReader.Abstractions</c> — are
/// resolved against the host's already-loaded copy so that the
/// <see cref="OpenReader.Abstractions.Plugins.IAppModule"/> the plugin
/// implements is the <em>same type</em> the host references. Without that,
/// a cast across the seam would throw <see cref="InvalidCastException"/>.
/// </para>
/// <para>
/// All other dependencies (the plugin's own DLL plus anything next to it)
/// are loaded into this context so they can be unloaded along with the
/// plugin when hot-reload kicks in.
/// </para>
/// </remarks>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDir;
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "OpenReader.Abstractions",
        // Diagnostics is shared so plugin Serilog log entries flow to the same sink.
        // (Plugins must not redistribute Serilog themselves.)
        "OpenReader.Diagnostics",
        "Serilog",
    };

    public PluginLoadContext(string pluginAssemblyPath, string pluginId)
        : base(name: $"OpenReader.Plugin:{pluginId}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
        _pluginDir = Path.GetDirectoryName(pluginAssemblyPath)
            ?? throw new ArgumentException("plugin path has no directory", nameof(pluginAssemblyPath));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is { } name && SharedAssemblies.Contains(name))
        {
            // Defer to the host (Default ALC). Returning null here means the
            // runtime falls back to the default context, which is the
            // behaviour we want for shared types.
            return null;
        }

        var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
        if (resolved is not null)
        {
            return LoadFromAssemblyPath(resolved);
        }

        // Last-resort: probe the plugin folder for a DLL with the matching name.
        if (assemblyName.Name is { } shortName)
        {
            var candidate = Path.Combine(_pluginDir, shortName + ".dll");
            if (File.Exists(candidate))
            {
                return LoadFromAssemblyPath(candidate);
            }
        }
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
