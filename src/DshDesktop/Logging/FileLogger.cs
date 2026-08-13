using System.IO;

namespace DshDesktop.Logging;

public sealed class FileLogger
{
    private readonly object _gate = new();
    private readonly string _filePath;

    public FileLogger(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} {ex}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                File.AppendAllText(_filePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志绝不能拖垮壳
        }
    }
}
