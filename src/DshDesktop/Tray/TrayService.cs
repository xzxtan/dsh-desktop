using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DshDesktop.Backend;
using Hardcodet.Wpf.TaskbarNotification;

namespace DshDesktop.Tray;

public sealed class TrayService : IDisposable
{
    private readonly BackendManager _backend;
    private readonly Window _window;
    private readonly TaskbarIcon _icon = new();
    private MenuItem _stateItem = null!;
    private MenuItem _restartItem = null!;

    public TrayService(BackendManager backend, Window window)
    {
        _backend = backend;
        _window = window;
        _backend.StateChanged += OnBackendStateChanged;
    }

    public void Initialize()
    {
        _icon.ToolTipText = "DeepSeek Harness";
        _icon.IconSource = TrayIcons.Starting;

        var menu = new ContextMenu();
        var show = new MenuItem { Header = "显示主窗口" };
        show.Click += (_, _) => ShowWindow();
        var openBrowser = new MenuItem { Header = "在浏览器中打开" };
        openBrowser.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(_backend.BaseUrl.ToString()) { UseShellExecute = true });
            }
            catch { /* 忽略 */ }
        };
        _stateItem = new MenuItem { Header = "状态：…", IsEnabled = false };
        _restartItem = new MenuItem { Header = "重启后端" };
        _restartItem.Click += async (_, _) => await _backend.RetryAsync();
        var settings = new MenuItem { Header = "设置" };
        settings.Click += (_, _) => new SettingsWindow().ShowDialog();
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => App.RequestExit();

        foreach (var item in new object[]
                 {
                     show, openBrowser, new Separator(), _stateItem, _restartItem, settings,
                     new Separator(), exit,
                 })
            menu.Items.Add(item);

        _icon.ContextMenu = menu;
        _icon.TrayMouseDoubleClick += (_, _) => ShowWindow();
        Update();
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void OnBackendStateChanged(BackendState _) => Update();

    private void Update()
    {
        if (!_window.Dispatcher.CheckAccess())
        {
            _window.Dispatcher.Invoke(Update);
            return;
        }

        _stateItem.Header = _backend.State switch
        {
            BackendState.Online => "状态：后端在线",
            BackendState.Offline => "状态：后端离线",
            BackendState.Failed => "状态：启动失败",
            _ => "状态：连接中…",
        };
        _restartItem.IsEnabled = _backend.OwnsBackend
            || _backend.State is BackendState.Offline or BackendState.Failed;
        // 用户裁决（2026-08-13）：自有后端崩溃后 OwnedProcessId 已置空，若只看 OwnsBackend
        // 菜单恰在最需要时变灰。恢复语义下 Offline/Failed 总是可重启；Online+attach 仍禁用。
        _icon.IconSource = _backend.State switch
        {
            BackendState.Online => TrayIcons.Online,
            BackendState.Offline or BackendState.Failed => TrayIcons.Offline,
            _ => TrayIcons.Starting,
        };
        _icon.ToolTipText = $"DeepSeek Harness — {_stateItem.Header}";
    }

    public void Dispose()
    {
        _backend.StateChanged -= OnBackendStateChanged;
        _icon.Dispose();
    }
}
