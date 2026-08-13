using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
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

        var placement = App.Settings;
        if (!double.IsNaN(placement.WindowLeft) && !double.IsNaN(placement.WindowTop))
        {
            Left = placement.WindowLeft;
            Top = placement.WindowTop;
        }
        Width = placement.WindowWidth;
        Height = placement.WindowHeight;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDarkTitleBar();
    }

    /// <summary>把标题栏切到深色模式，消除与深色 Web UI 之间的白色标题栏。</summary>
    private void ApplyDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var useDark = 1;
        // 属性 20 用于 Win10 2004+；失败回退旧属性 19（Win10 1809+）
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref useDark, sizeof(int));
    }

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public async Task InitAsync()
    {
        CoreWebView2Environment env;
        try
        {
            env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebView2UserDataDir);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowOverlay(
                "缺少 WebView2 运行时",
                "需要 Microsoft Edge WebView2 Evergreen Runtime。点击下方按钮前往微软官网下载安装。",
                showRetry: false);
            RetryButton.Content = "下载 WebView2";
            RetryButton.Visibility = Visibility.Visible;
            return;
        }
        RetryButton.Content = "重试";
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
        if (RetryButton.Content?.ToString() == "下载 WebView2")
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    "https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
            }
            catch { /* 忽略 */ }
            return;
        }
        ShowOverlay("正在重连…", "正在重新连接后端…", showRetry: false);
        var ok = await _backend.RetryAsync();
        if (ok && Browser.CoreWebView2 is not null)
            NavigateToBackend();
        else if (!ok)
            ShowOverlay("启动失败", "无法启动 dsh web。可在设置中配置 dsh 路径后重试。", showRetry: true);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow().ShowDialog();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
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
        var placement = App.Settings;
        placement.WindowLeft = Left;
        placement.WindowTop = Top;
        placement.WindowWidth = Width;
        placement.WindowHeight = Height;
        try
        {
            App.SettingsStore.Save(placement);
        }
        catch (Exception ex)
        {
            App.Log.Error("保存窗口位置失败", ex);
        }

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
