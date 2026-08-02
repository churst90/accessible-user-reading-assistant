using System.Runtime.Versioning;
using Microsoft.Win32;
using Serilog;

namespace Aura.Host;

/// <summary>
/// Writes (or removes) the Run-on-startup registry entry that launches
/// Aura when the user logs in.
/// </summary>
[SupportedOSPlatform("windows6.1")]
internal static class AutoStartRegistrar
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AURA";

    public static void Apply(bool enabled, ILogger log)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null)
            {
                log.Warning("could not open HKCU\\{Key}", RunKey);
                return;
            }

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                {
                    log.Warning("Environment.ProcessPath is empty; cannot register auto-start");
                    return;
                }
                key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
                log.Information("auto-start enabled → {Exe}", exe);
            }
            else
            {
                if (key.GetValue(ValueName) is not null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                    log.Information("auto-start disabled");
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            log.Warning(ex, "could not update auto-start registration");
        }
    }
}
