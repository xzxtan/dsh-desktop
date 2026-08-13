using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DshDesktop.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string FilePath { get; }

    public SettingsStore(string filePath) => FilePath = filePath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
            if (loaded is null) return new AppSettings();
            Normalize(loaded);
            return loaded;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            BackupCorruptFile();
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }

    private void BackupCorruptFile()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Copy(FilePath, FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
        }
        catch
        {
            // best-effort
        }
    }

    private static void Normalize(AppSettings s)
    {
        s.BackendPort = s.BackendPort is >= 1 and <= 65535 ? s.BackendPort : 3080;
        s.ReadyTimeoutSeconds = Math.Clamp(s.ReadyTimeoutSeconds, 1, 300);
        s.HealthIntervalSeconds = Math.Clamp(s.HealthIntervalSeconds, 1, 300);
        s.DshCommand = string.IsNullOrWhiteSpace(s.DshCommand) ? "dsh" : s.DshCommand;
        s.PageMarker = string.IsNullOrEmpty(s.PageMarker) ? "__DSH_BOOT__" : s.PageMarker;
        s.DshArgs ??= ["web"];
    }
}
