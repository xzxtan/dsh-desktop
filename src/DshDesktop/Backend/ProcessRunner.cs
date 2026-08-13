using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace DshDesktop.Backend;

public sealed class ProcessRunner : IProcessRunner
{
    private readonly ConcurrentDictionary<int, Process> _live = new();

    public int Start(string fileName, string arguments, TextWriter output)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{fileName}\" {arguments}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"无法启动进程: {fileName}");
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) output.WriteLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) output.WriteLine(e.Data);
        };
        process.Exited += (_, _) =>
        {
            try { output.Flush(); } catch { /* 忽略 */ }
            _live.TryRemove(process.Id, out _);
        };
        _live[process.Id] = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process.Id;
    }

    public void Stop(int processId)
    {
        try
        {
            using var killer = Process.Start(new ProcessStartInfo("taskkill.exe", $"/PID {processId} /T /F")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            killer?.WaitForExit(3000);
        }
        catch
        {
            // 进程可能已退出；忽略
        }
        _live.TryRemove(processId, out var p);
        p?.Dispose();
    }

    public bool IsRunning(int processId)
    {
        try
        {
            return _live.TryGetValue(processId, out var p) && !p.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
