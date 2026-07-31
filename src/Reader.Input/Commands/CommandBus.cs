using OpenReader.Diagnostics;
using Serilog;

namespace OpenReader.Input.Commands;

/// <summary>
/// Single-writer / many-reader dispatcher between the gesture map and command
/// handlers. Keeps the host wiring trivial and the gesture layer free of
/// downstream dependencies.
/// </summary>
public sealed class CommandBus
{
    private readonly object _gate = new();
    private readonly Dictionary<ReaderCommand, List<Func<CancellationToken, ValueTask>>> _handlers = new();
    private readonly ILogger _log;

    public CommandBus()
    {
        _log = LoggerFactory.ForComponent("Input.CommandBus");
    }

    /// <summary>Subscribe a handler for a specific command. Returns an unsubscribe disposable.</summary>
    public IDisposable Bind(ReaderCommand command, Func<CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            if (!_handlers.TryGetValue(command, out var list))
            {
                list = new List<Func<CancellationToken, ValueTask>>();
                _handlers[command] = list;
            }
            list.Add(handler);
        }
        return new Subscription(this, command, handler);
    }

    /// <summary>Dispatch a command to all bound handlers. Exceptions are logged and swallowed per handler.</summary>
    public async ValueTask DispatchAsync(ReaderCommand command, CancellationToken cancellationToken = default)
    {
        if (command == ReaderCommand.None)
        {
            return;
        }

        Func<CancellationToken, ValueTask>[] snapshot;
        lock (_gate)
        {
            if (!_handlers.TryGetValue(command, out var list) || list.Count == 0)
            {
                _log.Verbose("no handler bound for {Command}", command);
                return;
            }
            snapshot = list.ToArray();
        }

        foreach (var handler in snapshot)
        {
            try
            {
                await handler(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _log.Warning(ex, "handler threw on {Command}", command);
            }
        }
    }

    private void Unbind(ReaderCommand command, Func<CancellationToken, ValueTask> handler)
    {
        lock (_gate)
        {
            if (_handlers.TryGetValue(command, out var list))
            {
                list.Remove(handler);
                if (list.Count == 0)
                {
                    _handlers.Remove(command);
                }
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly CommandBus _bus;
        private readonly ReaderCommand _command;
        private readonly Func<CancellationToken, ValueTask> _handler;
        private bool _disposed;

        public Subscription(CommandBus bus, ReaderCommand command, Func<CancellationToken, ValueTask> handler)
        {
            _bus = bus;
            _command = command;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _bus.Unbind(_command, _handler);
        }
    }
}
