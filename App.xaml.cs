using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Moss;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\Moss.Desktop.Singleton";
    private const string OpenSettingsName = "Local\\Moss.Desktop.OpenSettings";
    private Mutex? _instanceMutex;
    private EventWaitHandle? _openSettingsSignal;
    private CancellationTokenSource? _shutdown;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instanceMutex = new Mutex(true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            try { EventWaitHandle.OpenExisting(OpenSettingsName).Set(); } catch { }
            Shutdown();
            return;
        }

        _openSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsName);
        _shutdown = new CancellationTokenSource();
        var settingsWindow = new MainWindow();
        MainWindow = settingsWindow;
        if (Array.Exists(e.Args, a => a.Equals("--background", StringComparison.OrdinalIgnoreCase)))
            settingsWindow.StartInBackground();
        else
            settingsWindow.Show();

        _ = Task.Run(() => ListenForOpenSettingsAsync(settingsWindow, _shutdown.Token));
    }

    private async Task ListenForOpenSettingsAsync(MainWindow window, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Run(() => _openSettingsSignal!.WaitOne(), cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                    _ = Dispatcher.BeginInvoke(new Action(window.OpenSettings));
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown?.Cancel();
        _openSettingsSignal?.Set();
        _openSettingsSignal?.Dispose();
        _shutdown?.Dispose();
        if (_ownsMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); } catch { }
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
