namespace X4ModLauncher;

public partial class App : System.Windows.Application
{
    private System.Threading.Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        const string mutexName = "Local\\X4ModLauncher.SingleInstance";
        _singleInstanceMutex = new System.Threading.Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("X4 Mod Launcher is already open.", "X4 Mod Launcher", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _ownsSingleInstanceMutex = true;
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        RecordException("Dispatcher", e.Exception);
        e.Handled = true;
        System.Windows.MessageBox.Show(
            $"The launcher caught an unexpected UI error and stayed open.\n\n{e.Exception.Message}\n\nA diagnostic entry was written to the X4ModLauncher log.",
            "X4 Mod Launcher — Unexpected Error",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            RecordException("AppDomain", exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        RecordException("Task", e.Exception);
        e.SetObserved();
    }

    private static void RecordException(string source, Exception exception)
    {
        try
        {
            var directory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "X4ModLauncher");
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "launcher.log");
            System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{exception}\n\n");
        }
        catch
        {
            // Diagnostics must never become a second crash path.
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
