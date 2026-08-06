using System.Runtime.Versioning;
using Aura.Abstractions.Accessibility;
using Aura.Abstractions.Plugins;

namespace Aura.Host;

/// <summary>
/// Resolves an <see cref="AccessibleNode"/>'s owning process: maps the UIA
/// <c>uia.ProcessId</c> extra to an executable name (cached) and builds a
/// <see cref="ProcessInfo"/> for the plugin host.
/// </summary>
/// <remarks>
/// Extracted from <c>Program.cs</c> to keep host wiring readable. Caches
/// per-pid lookups to avoid the cost of reopening a <see cref="System.Diagnostics.Process"/>
/// handle on every focus change.
/// </remarks>
[SupportedOSPlatform("windows6.1")]
internal sealed class FocusContextResolver
{
    private readonly Dictionary<int, string?> _pidToExe = new();

    /// <summary>Resolve an <see cref="AccessibleNode"/>'s executable name, or null if unavailable.</summary>
    public string? ResolveExecutableName(AccessibleNode node)
    {
        if (!TryGetPid(node, out var pid))
        {
            return null;
        }
        if (_pidToExe.TryGetValue(pid, out var cached))
        {
            return cached;
        }
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            var name = proc.ProcessName + ".exe";
            _pidToExe[pid] = name;
            return name;
        }
        catch (Exception)
        {
            _pidToExe[pid] = null;
            return null;
        }
    }

    /// <summary>Build a <see cref="ProcessInfo"/> for plugin attach/detach. Null when no pid is available.</summary>
    public static ProcessInfo? BuildProcessInfo(AccessibleNode node, string? executableName)
    {
        if (!TryGetPid(node, out var pid))
        {
            return null;
        }
        string? path = null;
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            try { path = proc.MainModule?.FileName; }
            catch { /* access denied for protected processes — leave null */ }
        }
        catch (Exception)
        {
            // process exited between focus and lookup — that's fine
        }
        return new ProcessInfo(
            ProcessId: pid,
            ExecutableName: executableName ?? string.Empty,
            ExecutablePath: path,
            WindowTitle: node.Name,
            AppUserModelId: null);
    }

    private static bool TryGetPid(AccessibleNode node, out int pid)
    {
        pid = 0;
        if (!node.Extras.TryGetValue(NodeExtras.ProcessId, out var raw) || raw is not int p || p == 0)
        {
            return false;
        }
        pid = p;
        return true;
    }
}
