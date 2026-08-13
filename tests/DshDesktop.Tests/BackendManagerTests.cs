using System.Collections.Concurrent;
using DshDesktop.Backend;
using DshDesktop.Logging;
using DshDesktop.Settings;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace DshDesktop.Tests;

public sealed class BackendManagerTests
{
    private sealed class FakeProbe : IBackendProbe
    {
        public readonly ConcurrentQueue<ProbeResult> Results = new();

        public Task<ProbeResult> ProbeAsync(Uri baseUrl, string marker, CancellationToken ct = default) =>
            Task.FromResult(Results.TryDequeue(out var r) ? r : ProbeResult.NotReady);
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public readonly List<(string FileName, string Args)> Started = new();
        public readonly List<int> Stopped = new();
        public bool ProcessAlive = true;

        public int Start(string fileName, string arguments, TextWriter output)
        {
            Started.Add((fileName, arguments));
            return 1234;
        }

        public void Stop(int processId) => Stopped.Add(processId);

        public bool IsRunning(int processId) => ProcessAlive;
    }

    private static FileLogger NullLog() =>
        new(Path.Combine(Path.GetTempPath(), "dsh-tests", Guid.NewGuid().ToString("N"), "app.log"));

    private static (BackendManager Manager, FakeProbe Probe, FakeRunner Runner, FakeTimeProvider Time) NewManager(
        AppSettings? settings = null)
    {
        var probe = new FakeProbe();
        var runner = new FakeRunner();
        var time = new FakeTimeProvider();
        var manager = new BackendManager(settings ?? new AppSettings(), probe, runner, NullLog(), time);
        return (manager, probe, runner, time);
    }

    [Fact]
    public async Task EnsureStarted_Attaches_WhenBackendAlreadyReady()
    {
        var (manager, probe, runner, _) = NewManager();
        probe.Results.Enqueue(ProbeResult.Ready);

        var ok = await manager.EnsureStartedAsync();

        Assert.True(ok);
        Assert.Equal(BackendState.Online, manager.State);
        Assert.False(manager.OwnsBackend);
        Assert.Empty(runner.Started);
        Assert.Empty(runner.Stopped);
    }

    [Fact]
    public async Task EnsureStarted_Spawns_WhenPortFree()
    {
        var (manager, probe, runner, time) = NewManager();
        probe.Results.Enqueue(ProbeResult.NotReady);
        probe.Results.Enqueue(ProbeResult.Ready);

        var task = manager.EnsureStartedAsync();
        time.Advance(TimeSpan.FromSeconds(1));
        var ok = await task;

        Assert.True(ok);
        Assert.Equal(BackendState.Online, manager.State);
        Assert.True(manager.OwnsBackend);
        Assert.Equal(1234, manager.OwnedProcessId);
        Assert.Single(runner.Started);
        Assert.Equal("dsh", runner.Started[0].FileName);
        Assert.Equal("web", runner.Started[0].Args);
    }

    [Fact]
    public async Task EnsureStarted_Fails_WhenSpawnTimesOut()
    {
        var (manager, probe, runner, time) = NewManager(new AppSettings { ReadyTimeoutSeconds = 2 });
        // probe 队列为空 → 恒为 NotReady

        var task = manager.EnsureStartedAsync();
        time.Advance(TimeSpan.FromSeconds(5));
        var ok = await task;

        Assert.False(ok);
        Assert.Equal(BackendState.Failed, manager.State);
        Assert.Contains(1234, runner.Stopped);
        Assert.False(manager.OwnsBackend);
    }

    [Fact]
    public async Task EnsureStarted_Fails_OnForeignServer()
    {
        var (manager, probe, runner, _) = NewManager();
        probe.Results.Enqueue(ProbeResult.ForeignServer);

        var ok = await manager.EnsureStartedAsync();

        Assert.False(ok);
        Assert.Equal(BackendState.Failed, manager.State);
        Assert.Empty(runner.Started);
    }

    [Fact]
    public async Task HealthMonitor_GoesOffline_AfterThreeFailures_AndRecovers()
    {
        var (manager, probe, _, time) = NewManager(new AppSettings { HealthIntervalSeconds = 5 });
        probe.Results.Enqueue(ProbeResult.Ready);
        Assert.True(await manager.EnsureStartedAsync());

        time.Advance(TimeSpan.FromSeconds(16)); // 3 次探测均 NotReady
        Assert.Equal(BackendState.Offline, manager.State);

        probe.Results.Enqueue(ProbeResult.Ready);
        time.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(BackendState.Online, manager.State);
    }

    [Fact]
    public async Task Retry_StopsOwnedThenReattaches()
    {
        var (manager, probe, runner, time) = NewManager();
        probe.Results.Enqueue(ProbeResult.NotReady);
        probe.Results.Enqueue(ProbeResult.Ready);
        var task = manager.EnsureStartedAsync();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await task);
        probe.Results.Enqueue(ProbeResult.Ready);

        var ok = await manager.RetryAsync();

        Assert.True(ok);
        Assert.Contains(1234, runner.Stopped);
        Assert.False(manager.OwnsBackend);
    }

    [Fact]
    public async Task StopOwnedBackend_NoOp_WhenAttachedOnly()
    {
        var (manager, probe, runner, _) = NewManager();
        probe.Results.Enqueue(ProbeResult.Ready);
        Assert.True(await manager.EnsureStartedAsync());

        manager.StopOwnedBackend();

        Assert.Empty(runner.Stopped);
    }
}
