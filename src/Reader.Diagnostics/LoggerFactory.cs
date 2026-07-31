using System.Globalization;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace OpenReader.Diagnostics;

/// <summary>
/// Builds the process-wide Serilog logger and produces named child loggers
/// for individual components.
/// </summary>
/// <remarks>
/// Call <see cref="Configure"/> exactly once at host startup. After that,
/// every component should obtain its logger via <see cref="ForComponent"/>.
/// </remarks>
public static class LoggerFactory
{
    private static Logger? _root;
    private static readonly object _gate = new();

    /// <summary>
    /// Live minimum level. The logger is configured before config is loaded
    /// (so that config-load failures are themselves logged), so the level has
    /// to be adjustable afterwards rather than fixed at construction.
    /// </summary>
    private static readonly LoggingLevelSwitch _levelSwitch = new(LogEventLevel.Information);

    /// <summary>
    /// Raise or lower the minimum level at runtime. Takes effect immediately
    /// on the already-built logger.
    /// </summary>
    public static void SetMinimumLevel(LogEventLevel level) => _levelSwitch.MinimumLevel = level;

    /// <summary>
    /// Parse a config string (<c>verbose</c>, <c>debug</c>, <c>information</c>,
    /// <c>warning</c>, <c>error</c>, <c>fatal</c>) and apply it. Unrecognised
    /// values leave the level unchanged.
    /// </summary>
    public static void SetMinimumLevel(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return;
        }
        switch (level.Trim().ToUpperInvariant())
        {
            case "VERBOSE" or "TRACE": SetMinimumLevel(LogEventLevel.Verbose); break;
            case "DEBUG": SetMinimumLevel(LogEventLevel.Debug); break;
            case "INFORMATION" or "INFO": SetMinimumLevel(LogEventLevel.Information); break;
            case "WARNING" or "WARN": SetMinimumLevel(LogEventLevel.Warning); break;
            case "ERROR": SetMinimumLevel(LogEventLevel.Error); break;
            case "FATAL": SetMinimumLevel(LogEventLevel.Fatal); break;
        }
    }

    /// <summary>Configure the global root logger. Idempotent — additional calls are ignored.</summary>
    public static void Configure(LogEventLevel minimumLevel = LogEventLevel.Information, bool console = false)
    {
        lock (_gate)
        {
            if (_root is not null)
            {
                return;
            }
            _levelSwitch.MinimumLevel = minimumLevel;

            var cfg = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(_levelSwitch)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path: LogPaths.LogFileTemplate,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    formatProvider: CultureInfo.InvariantCulture,
                    outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Component} {Message:lj}{NewLine}{Exception}");

            if (console)
            {
                cfg = cfg.WriteTo.Console(
                    formatProvider: CultureInfo.InvariantCulture,
                    outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Component} {Message:lj}{NewLine}{Exception}");
            }

            _root = cfg.CreateLogger();
            Log.Logger = _root;
        }
    }

    /// <summary>Obtain a logger tagged with the given component name.</summary>
    public static ILogger ForComponent(string component)
    {
        if (_root is null)
        {
            Configure();
        }

        return Log.Logger.ForContext("Component", component);
    }

    /// <summary>Flush and dispose the root logger. Safe to call once at shutdown.</summary>
    public static void Shutdown()
    {
        lock (_gate)
        {
            _root?.Dispose();
            _root = null;
            Log.CloseAndFlush();
        }
    }
}
