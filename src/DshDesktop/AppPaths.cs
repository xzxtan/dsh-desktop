using System.IO;

namespace DshDesktop;

public static class AppPaths
{
    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "dsh-desktop");

    public static string LogsDir => Path.Combine(AppDataDir, "logs");

    public static string SettingsFile => Path.Combine(AppDataDir, "settings.json");

    public static string WebView2UserDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "dsh-desktop", "WebView2");
}
