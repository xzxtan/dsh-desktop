using System.IO;
using DshDesktop.Logging;
using DshDesktop.Settings;

namespace DshDesktop.Backend;

public enum BackendState
{
    Idle,
    Attached,
    Spawning,
    WaitingReady,
    Online,
    Offline,
    Failed,
}

public interface IProcessRunner
{
    int Start(string fileName, string arguments, TextWriter output);
    void Stop(int processId);
    bool IsRunning(int processId);
}

public sealed class BackendManager : IDisposable
{
    private readonly AppSettings _settings;
    private readonly IBackendProbe _probe;
    private readonly IProcessRunner _runner;
    private readonly FileLogger _log;
    private readonly TimeProvider _time;
    private CancellationTokenSource? _healthCts;

    public BackendState State { get; private set; } = BackendState.Idle;
    public int? OwnedProcessId { get; private set; }
    public bool OwnsBackend => OwnedProcessId is not null;
    public Uri BaseUrl => _settings.BackendBaseUrl;
    public event Action<BackendState>? StateChanged;

    public BackendManager(
        AppSettings settings,
        IBackendProbe probe,
        IProcessRunner runner,
        FileLogger log,
        TimeProvider? time = null)
    {
        _settings = settings;
        _probe = probe;
        _runner = runner;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    public async Task<bool> EnsureStartedAsync(CancellationToken ct = default)
    {
        var first = await _probe.ProbeAsync(BaseUrl, _settings.PageMarker, ct);
        switch (first)
        {
            case ProbeResult.Ready:
                _log.Info($"端口 {_settings.BackendPort} 已就绪，attach 已有后端");
                Transition(BackendState.Attached);
                Transition(BackendState.Online);
                StartHealthMonitoring();
                return true;
            case ProbeResult.ForeignServer:
                _log.Warn($"端口 {_settings.BackendPort} 被非 Harness 服务占用");
                Transition(BackendState.Failed);
                return false;
        }

        Transition(BackendState.Spawning);
        _log.Info($"启动后端: {_settings.DshCommand} {string.Join(' ', _settings.DshArgs)}");
        int pid;
        try
        {
            var args = string.Join(' ', _settings.DshArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            pid = _runner.Start(_settings.DshCommand, args, CreateBackendLogWriter());
        }
        catch (Exception ex)
        {
            _log.Error($"后端启动失败（找不到 {_settings.DshCommand}？）", ex);
            Transition(BackendState.Failed);
            return false;
        }
        OwnedProcessId = pid;

        Transition(BackendState.WaitingReady);
        var deadline = _time.GetUtcNow() + TimeSpan.FromSeconds(_settings.ReadyTimeoutSeconds);
        while (_time.GetUtcNow() < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), _time, ct);
            var result = await _probe.ProbeAsync(BaseUrl, _settings.PageMarker, ct);
            if (result == ProbeResult.Ready)
            {
                _log.Info("后端就绪");
                Transition(BackendState.Online);
                StartHealthMonitoring();
                return true;
            }
            if (result == ProbeResult.ForeignServer)
            {
                _log.Warn($"端口 {_settings.BackendPort} 被非 Harness 服务占用");
                StopOwnedBackend();
                Transition(BackendState.Failed);
                return false;
            }
        }

        _log.Error($"后端 {_settings.ReadyTimeoutSeconds}s 内未就绪");
        StopOwnedBackend();
        Transition(BackendState.Failed);
        return false;
    }

    public async Task<bool> RetryAsync(CancellationToken ct = default)
    {
        StopOwnedBackend();
        return await EnsureStartedAsync(ct);
    }

    public void StopOwnedBackend()
    {
        if (OwnedProcessId is { } pid)
        {
            _log.Info($"停止后端进程 {pid}");
            _runner.Stop(pid);
            OwnedProcessId = null;
        }
    }

    private TextWriter CreateBackendLogWriter()
    {
        Directory.CreateDirectory(AppPaths.LogsDir);
        var path = Path.Combine(AppPaths.LogsDir, "backend.log");
        return new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };
        // 该 writer 被进程事件处理器持有，与进程同生命周期；壳不额外回收。
    }

    private void StartHealthMonitoring()
    {
        _healthCts?.Cancel();
        _healthCts = new CancellationTokenSource();
        _ = MonitorLoopAsync(_healthCts.Token);
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        var failures = 0;
        var interval = TimeSpan.FromSeconds(_settings.HealthIntervalSeconds);
        // 用绝对时间点驱动探测，使 FakeTimeProvider.Advance() 能同步触发多次探测
        // （相对 Task.Delay 在时钟一次性前跳后只会触发一次）。
        var next = _time.GetUtcNow() + interval;
        while (!ct.IsCancellationRequested)
        {
            var wait = next - _time.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(wait, _time, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            next += interval;

            if (OwnedProcessId is { } pid && !_runner.IsRunning(pid))
            {
                _log.Warn($"后端进程 {pid} 已退出");
                OwnedProcessId = null;
                failures = 3; // 直接判离线，不再等三次探测
            }
            else
            {
                var result = await _probe.ProbeAsync(BaseUrl, _settings.PageMarker, ct);
                failures = result == ProbeResult.Ready ? 0 : failures + 1;
            }

            if (failures >= 3 && State != BackendState.Offline)
                Transition(BackendState.Offline);
            else if (failures == 0 && State == BackendState.Offline)
                Transition(BackendState.Online);
        }
    }

    private void Transition(BackendState next)
    {
        if (State == next) return;
        State = next;
        StateChanged?.Invoke(next);
    }

    public void Dispose()
    {
        _healthCts?.Cancel();
        _healthCts?.Dispose();
    }
}
