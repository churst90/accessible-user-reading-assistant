using System.Runtime.Versioning;
using System.Windows.Threading;
using Aura.Diagnostics;

namespace Aura.Host;

/// <summary>
/// Owns a dedicated STA thread that runs a WPF <see cref="Dispatcher"/>'s
/// message pump. The host's tray icon and dialogs live on this thread; without
/// a running pump <c>BeginInvoke</c> calls would queue forever.
/// </summary>
/// <remarks>
/// Built specifically because the Main thread blocks in the speech-queue drain
/// loop and never returns to a pump. A dedicated UI thread keeps the two
/// concerns independent: speech keeps draining, dialogs/tray stay responsive.
///
/// <para>
/// <b>Unhandled exception policy.</b> Hooks <c>Dispatcher.UnhandledException</c>
/// to log and swallow click-handler / dispatched-callback failures so a
/// future bug like the <c>DialogResult</c> regression can't take down the
/// host process.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows6.1")]
internal sealed class UiThread : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private Dispatcher? _dispatcher;
    private bool _disposed;

    public UiThread()
    {
        _thread = new Thread(Run)
        {
            IsBackground = false,
            Name = "Aura.UI",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public Dispatcher Dispatcher =>
        _dispatcher ?? throw new InvalidOperationException("UI thread not started");

    private void Run()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        var log = LoggerFactory.ForComponent("UI");
        _dispatcher.UnhandledException += (_, e) =>
        {
            log.Error(e.Exception, "WPF dispatcher swallowed unhandled exception");
            e.Handled = true;
        };
        _ready.Set();
        Dispatcher.Run();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _dispatcher?.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
