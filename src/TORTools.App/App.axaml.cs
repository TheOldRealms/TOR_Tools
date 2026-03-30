using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TORTools.App.Views;
using TORTools.App.ViewModels;

namespace TORTools.App;

public partial class App : Application
{
    private FileSystemWatcher? _shutdownWatcher;
    private static readonly string ShutdownSignalPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shutdown.signal");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            // Setup shutdown signal watcher for remote shutdown
            SetupShutdownWatcher(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupShutdownWatcher(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            // Clean up any existing signal file
            var signalPath = Path.GetFullPath(ShutdownSignalPath);
            if (File.Exists(signalPath))
                File.Delete(signalPath);

            var directory = Path.GetDirectoryName(signalPath);
            if (directory == null || !Directory.Exists(directory))
                return;

            _shutdownWatcher = new FileSystemWatcher(directory, "shutdown.signal")
            {
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            _shutdownWatcher.Created += (s, e) =>
            {
                // Shutdown on UI thread
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Clean up signal file
                    try { File.Delete(e.FullPath); } catch { }
                    desktop.Shutdown();
                });
            };
        }
        catch
        {
            // Ignore errors in shutdown watcher setup
        }
    }
}
