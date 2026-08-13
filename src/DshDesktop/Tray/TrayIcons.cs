using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DshDesktop.Tray;

public static class TrayIcons
{
    public static ImageSource Online { get; } = Make(0xFF2E7D32);
    public static ImageSource Offline { get; } = Make(0xFFC62828);
    public static ImageSource Starting { get; } = Make(0xFF9E9E9E);

    private static ImageSource Make(uint argb)
    {
        const int size = 16;
        var a = (byte)(argb >> 24);
        var r = (byte)(argb >> 16);
        var g = (byte)(argb >> 8);
        var b = (byte)argb;
        var value = (uint)((a << 24) | (r << 16) | (g << 8) | b); // BGRA 内存序
        var pixels = new uint[size * size];
        Array.Fill(pixels, value);
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bmp.Freeze();
        return bmp;
    }
}
