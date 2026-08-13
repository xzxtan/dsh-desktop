using System.Windows;
using DshDesktop.Backend;
using DshDesktop.Logging;
using DshDesktop.Settings;

namespace DshDesktop;

public partial class App : Application
{
    public static bool IsExiting { get; private set; }
    public static FileLogger Log = null!;
    public static SettingsStore SettingsStore = null!;
    public static AppSettings Settings = null!;
    public static BackendManager Backend = null!;

    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log = new FileLogger(System.IO.Path.Combine(AppPaths.LogsDir, "dsh-desktop.log"));
        SettingsStore = new SettingsStore(AppPaths.SettingsFile);
        Settings = SettingsStore.Load();
        Log.Info($"启动 dsh-desktop，参数: {string.Join(' ', e.Args)}");

        Backend = new BackendManager(Settings, new HttpBackendProbe(), new ProcessRunner(), Log);
        Backend.StateChanged += state => Log.Info($"后端状态: {state}");

        _mainWindow = new MainWindow(Backend);
        MainWindow = _mainWindow;
        _mainWindow.Show();
        await _mainWindow.InitAsync();

        var started = await Backend.EnsureStartedAsync();
        Log.Info(started ? "后端就绪" : "后端未就绪（离线覆盖层）");
    }

    public static void RequestExit()
    {
        IsExiting = true;
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        if (Settings.StopSpawnedBackendOnExit)
            Backend.StopOwnedBackend();
        Backend.Dispose();
        Log.Info("退出");
        base.OnExit(e);
    }
}
