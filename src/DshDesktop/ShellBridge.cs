using System.Runtime.InteropServices;
using System.Windows;

namespace DshDesktop;

/// <summary>
/// 暴露给 WebView2 页面注入脚本的窗口控制桥：
/// 最小化 / 最大化切换 / 关闭 / 开始拖动（WM_NCLBUTTONDOWN + HTCAPTION）。
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public sealed class ShellBridge
{
    private readonly MainWindow _window;

    public ShellBridge(MainWindow window) => _window = window;

    public void Minimize() =>
        _window.Dispatcher.Invoke(() => _window.WindowState = WindowState.Minimized);

    public void ToggleMaximize() =>
        _window.Dispatcher.Invoke(() =>
            _window.WindowState = _window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized);

    public void Close() =>
        _window.Dispatcher.Invoke(_window.Close);

    public void BeginDrag() =>
        _window.Dispatcher.Invoke(_window.BeginWindowDrag);
}
