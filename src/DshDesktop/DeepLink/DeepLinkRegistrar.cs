using Microsoft.Win32;

namespace DshDesktop.DeepLink;

public static class DeepLinkRegistrar
{
    public const string Scheme = "dsh-desktop";

    public static void Register(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
        key.SetValue(null, "URL:DeepSeek Harness Desktop");
        key.SetValue("URL Protocol", "");
        using var cmd = key.CreateSubKey(@"shell\open\command");
        cmd.SetValue(null, $"\"{exePath}\" \"%1\"");
    }

    public static string? GetRegisteredCommand()
    {
        using var cmd = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Scheme}\shell\open\command");
        return cmd?.GetValue(null) as string;
    }
}
