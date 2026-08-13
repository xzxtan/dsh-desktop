using System.Windows.Media.Imaging;

namespace DshDesktop.Tray;

/// <summary>
/// 托盘三态图标：均使用 DSH 鲸鱼图形（源自 dsh-web-frontend 的 favicon.svg），
/// 用底色区分状态——在线=品牌蓝、离线=红、启动中=灰。
/// </summary>
public static class TrayIcons
{
    public static BitmapSource Online { get; } = Load("tray-online.png");
    public static BitmapSource Offline { get; } = Load("tray-offline.png");
    public static BitmapSource Starting { get; } = Load("tray-starting.png");

    private static BitmapSource Load(string name)
    {
        var bmp = new BitmapImage(new Uri($"pack://application:,,,/assets/{name}"));
        bmp.Freeze();
        return bmp;
    }
}
