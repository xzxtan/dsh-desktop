using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using DshDesktop.Backend;
using Microsoft.Web.WebView2.Core;

namespace DshDesktop;

public partial class MainWindow : Window
{
    private readonly BackendManager _backend;

    public MainWindow(BackendManager backend)
    {
        _backend = backend;
        InitializeComponent();
        _backend.StateChanged += OnBackendStateChanged;
    }

    public async Task InitAsync()
    {
        var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebView2UserDataDir);
        await Browser.EnsureCoreWebView2Async(env);
        Browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
        Browser.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
            }
            catch
            {
                // 忽略打不开的链接
            }
        };
        Browser.NavigationCompleted += Browser_NavigationCompleted;

        if (_backend.State == BackendState.Online)
            NavigateToBackend();
        else
            ShowOverlay("正在启动 DeepSeek Harness…", "正在连接后端…", showRetry: false);
    }

    private void NavigateToBackend()
    {
        var url = _backend.BaseUrl;
        // CoreWebView2.Source 是 string，而 WPF 控件的 Browser.Source 是 Uri —— 用后者比较。
        if (Browser.CoreWebView2 is not null && Browser.Source != url)
            Browser.CoreWebView2.Navigate(url.ToString());
        HideOverlay();
    }

    private void OnBackendStateChanged(BackendState state)
    {
        Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case BackendState.Spawning:
                case BackendState.WaitingReady:
                    ShowOverlay("正在启动 DeepSeek Harness…", "后端未运行，正在自动拉起 dsh web", showRetry: false);
                    break;
                case BackendState.Online:
                    // InitAsync 末尾对「Online 先于 CoreWebView2 就绪」的情形兜底导航。
                    if (Browser.CoreWebView2 is not null)
                        NavigateToBackend();
                    HideOverlay();
                    break;
                case BackendState.Offline:
                    ShowOverlay("后端未连接", "DeepSeek Harness 已停止。点击重试重新连接。", showRetry: true);
                    break;
                case BackendState.Failed:
                    ShowOverlay("启动失败", "无法启动 dsh web。可在设置中配置 dsh 路径后重试。", showRetry: true);
                    break;
            }
        });
    }

    private void Browser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
            ShowOverlay("页面加载失败", "后端可能已离线，点击重试。", showRetry: true);
    }

    private void ShowOverlay(string title, string detail, bool showRetry)
    {
        OverlayTitle.Text = title;
        OverlayDetail.Text = detail;
        RetryButton.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
        Overlay.Visibility = Visibility.Visible;
        Browser.Visibility = Visibility.Collapsed;
    }

    private void HideOverlay()
    {
        Overlay.Visibility = Visibility.Collapsed;
        Browser.Visibility = Visibility.Visible;
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        ShowOverlay("正在重连…", "正在重新连接后端…", showRetry: false);
        var ok = await _backend.RetryAsync();
        if (ok && Browser.CoreWebView2 is not null)
            NavigateToBackend();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // Task 9 引入设置对话框；当前阶段直接打开 settings.json 所在目录提示
        MessageBox.Show(
            $"设置文件: {AppPaths.SettingsFile}\n\n（设置对话框将在后续任务提供）",
            "设置", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F12 && Browser.CoreWebView2 is not null)
            Browser.CoreWebView2.OpenDevToolsWindow();
        else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.Control)
            Browser.CoreWebView2?.Reload();
        base.OnKeyDown(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (App.IsExiting) return;
        if (App.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            App.RequestExit(); // 直接退出整个应用
        }
        base.OnClosing(e);
    }
}
