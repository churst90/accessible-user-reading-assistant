using System.Diagnostics;
using Serilog;
using Serilog.Events;

namespace Aura.Diagnostics;

/// <summary>
/// Lightweight scoped stopwatch for hot-path timing. Logs elapsed time at
/// <see cref="LogEventLevel.Debug"/> when disposed; logs a warning if the
/// budget is exceeded.
/// </summary>
/// <remarks>
/// Allocates one <see cref="Stopwatch"/> per scope. Acceptable for event-rate
/// hot paths; for inner-loop timing, prefer <see cref="Stopwatch.GetTimestamp"/>
/// directly to stay allocation-free.
/// </remarks>
public readonly struct PerfTimer : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _operation;
    private readonly TimeSpan _budget;
    private readonly Stopwatch _sw;

    public PerfTimer(ILogger logger, string operation, TimeSpan budget)
    {
        _logger = logger;
        _operation = operation;
        _budget = budget;
        _sw = Stopwatch.StartNew();
    }

    public static PerfTimer Start(ILogger logger, string operation, TimeSpan budget)
        => new(logger, operation, budget);

    public void Dispose()
    {
        _sw.Stop();
        var elapsed = _sw.Elapsed;
        if (elapsed > _budget)
        {
            _logger.Warning("perf: {Operation} took {ElapsedMs:F2}ms (budget {BudgetMs:F2}ms)", _operation, elapsed.TotalMilliseconds, _budget.TotalMilliseconds);
        }
        else if (_logger.IsEnabled(LogEventLevel.Debug))
        {
            _logger.Debug("perf: {Operation} took {ElapsedMs:F2}ms", _operation, elapsed.TotalMilliseconds);
        }
    }
}
