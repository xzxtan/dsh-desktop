using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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

    /// <summary>
    /// 注入到 Harness 页面的脚本（每个文档创建时注册，DOMContentLoaded 后执行，幂等自愈）：
    /// 1) 右上角悬浮窗口按钮簇（─ □ ✕），经 hostObjects.dshShell 桥接宿主；
    /// 2) 给页面顶栏右侧预留 118px，避免按钮簇压住 Session log / 详情抽屉；
    /// 3) 顶栏 76px 空白区拖动窗口（排除交互元素），双击切换最大化。
    /// 注意：document-created 阶段 head/documentElement 尚为 null，必须延迟到 DOMContentLoaded；
    /// dev 版 HMR 会整页热重载，MutationObserver + 2s 定时器保证簇被抹掉后自动重建。
    /// </summary>
    private const string ShellInjection = """
        (() => {
          if (window.__dshShellInjected) return;
          window.__dshShellInjected = true;

          const css = `
          /* 颜色全部引用页面主题令牌（body 上的 --dsw-alias-*），暗/亮主题自动一致 */
          #dsh-shell-cluster { position: fixed; top: 2px; right: 2px; z-index: 2147483000;
            display: flex; gap: 2px; height: 28px; padding: 0 2px; border-radius: 8px;
            background: var(--dsw-alias-bg-layer-1, rgba(20,24,38,0.92));
            border: 1px solid var(--dsw-alias-border-l2, rgba(255,255,255,0.12));
            -webkit-user-select: none; user-select: none; }
          #dsh-shell-cluster button { all: unset; width: 36px; height: 26px; display: inline-flex;
            align-items: center; justify-content: center;
            color: var(--dsw-alias-label-primary, #F9FAFB);
            font: 12px 'Segoe MDL2 Assets'; cursor: default; border-radius: 6px; }
          #dsh-shell-cluster button:hover { background: var(--dsw-alias-bg-layer-2, rgba(255,255,255,0.14)); }
          #dsh-shell-cluster button#dsh-shell-close:hover {
            background: var(--dsw-alias-state-error-primary, #C42B1C); color: #fff; }
          .wSkVaW_headerUtilities { margin-right: 118px !important; }
          .ydkMvW_header { padding-right: 118px !important; }
          `;

          const shell = () => window.chrome?.webview?.hostObjects?.dshShell;

          const ensure = () => {
            try {
              if (!document.getElementById('dsh-shell-style')) {
                const style = document.createElement('style');
                style.id = 'dsh-shell-style';
                style.textContent = css;
                (document.head || document.documentElement).appendChild(style);
              }
              if (!document.getElementById('dsh-shell-cluster')) {
                const cluster = document.createElement('div');
                cluster.id = 'dsh-shell-cluster';
                const mk = (label, id, action) => {
                  const b = document.createElement('button');
                  b.id = id; b.textContent = label;
                  b.addEventListener('mousedown', e => { e.stopPropagation(); e.preventDefault(); });
                  b.addEventListener('click', e => { e.stopPropagation(); e.preventDefault(); action(); });
                  return b;
                };
                cluster.append(
                  mk('\uE921', 'dsh-shell-min', () => shell()?.Minimize()),
                  mk('\uE922', 'dsh-shell-max', () => shell()?.ToggleMaximize()),
                  mk('\uE8BB', 'dsh-shell-close', () => shell()?.Close())
                );
                (document.body || document.documentElement).appendChild(cluster);
              }
              if (!document.__dshShellListeners) {
                document.__dshShellListeners = true;
                const INTERACTIVE = 'button,a,input,textarea,select,label,[role="button"],#dsh-shell-cluster';
                let down = null;
                document.addEventListener('mousedown', e => {
                  if (e.button !== 0 || e.clientY > 76) return;
                  const t = e.target;
                  if (t && t.closest && t.closest(INTERACTIVE)) return;
                  down = { x: e.clientX, y: e.clientY, dragged: false };
                }, true);
                document.addEventListener('mousemove', e => {
                  if (!down || down.dragged) return;
                  if (Math.abs(e.clientX - down.x) + Math.abs(e.clientY - down.y) > 4) {
                    down.dragged = true;
                    window.__dshDragCalls = (window.__dshDragCalls || 0) + 1;
                    shell()?.BeginDrag();
                  }
                }, true);
                document.addEventListener('mouseup', () => { down = null; }, true);
                document.addEventListener('dblclick', e => {
                  if (e.clientY > 76) return;
                  const t = e.target;
                  if (t && t.closest && t.closest(INTERACTIVE)) return;
                  shell()?.ToggleMaximize();
                }, true);
              }
            } catch (err) {
              console.error('dsh-shell injection failed:', err);
            }
          };

          const boot = () => {
            ensure();
            try {
              new MutationObserver(() => {
                if (!document.getElementById('dsh-shell-style') || !document.getElementById('dsh-shell-cluster'))
                  ensure();
              }).observe(document.documentElement, { childList: true, subtree: true });
            } catch { /* 文档被整体替换时观察器失效，由下面的定时器兜底 */ }
            setInterval(() => {
              if (!document.getElementById('dsh-shell-style') || !document.getElementById('dsh-shell-cluster'))
                ensure();
            }, 2000);
          };

          if (document.readyState === 'loading')
            document.addEventListener('DOMContentLoaded', boot, { once: true });
          else boot();
        })();
        """;

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
        var core = Browser.CoreWebView2;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreHostObjectsAllowed = true;
        core.AddHostObjectToScript("dshShell", new ShellBridge(this));
        await core.AddScriptToExecuteOnDocumentCreatedAsync(ShellInjection);
        core.NewWindowRequested += (_, e) =>
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

    /// <summary>覆盖层空白区按下 = 拖动窗口（按钮区域除外）。</summary>
    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource)) return;
        BeginWindowDrag();
    }

    private static bool IsInsideButton(object? source)
    {
        for (var d = source as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is Button) return true;
        return false;
    }

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HT_CAPTION = 2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// 进入系统原生移动循环（含 Aero Snap 分屏/贴靠）。
    /// 关键：先 ReleaseCapture 释放 WebView2 子窗口的鼠标捕获，
    /// 否则合成 WM_NCLBUTTONDOWN 进不了 DefWindowProc 的移动循环。
    /// </summary>
    internal void BeginWindowDrag()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HT_CAPTION), IntPtr.Zero);
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
