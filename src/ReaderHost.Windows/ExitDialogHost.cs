using System.Runtime.Versioning;
using System.Windows.Threading;
using Aura.UI.Dialogs;
using Serilog;

namespace Aura.Host;

/// <summary>
/// Owner for the WPF Yes/No exit dialog. On Yes, signals the host's
/// shutdown <see cref="CancellationTokenSource"/> so the main loop unwinds
/// cleanly.
/// </summary>
[SupportedOSPlatform("windows6.1")]
internal sealed class ExitDialogHost
{
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _shutdown;
    private readonly ILogger _log;
    private ExitDialog? _open;

    public ExitDialogHost(Dispatcher dispatcher, CancellationTokenSource shutdown, ILogger log)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
        _log = log;
    }

    public void Show()
    {
        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_open is { IsLoaded: true })
                {
                    _open.Activate();
                    return;
                }
                var dialog = new ExitDialog();
                dialog.Closed += (_, _) =>
                {
                    _open = null;
                    if (dialog.ConfirmExit)
                    {
                        _log.Information("user confirmed exit");
                        _shutdown.Cancel();
                    }
                };
                _open = dialog;
                dialog.Show();
                ForegroundWindowHelper.BringToFront(dialog);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _log.Warning(ex, "could not open exit dialog");
            }
        });
    }
}
