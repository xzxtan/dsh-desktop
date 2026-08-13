using DshDesktop.SingleInstance;
using Xunit;

namespace DshDesktop.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void Acquire_FirstInstance_Wins()
    {
        var name = Guid.NewGuid().ToString("N");
        using var guard = SingleInstanceGuard.Acquire(name);

        Assert.True(guard.IsFirstInstance);
    }

    [Fact]
    public void Acquire_SecondInstance_WithSameName_Loses()
    {
        var name = Guid.NewGuid().ToString("N");
        using var first = SingleInstanceGuard.Acquire(name);
        using var second = SingleInstanceGuard.Acquire(name);

        Assert.True(first.IsFirstInstance);
        Assert.False(second.IsFirstInstance);
    }

    [Fact]
    public async Task SendArgs_DeliversToListener()
    {
        var pipeName = "dsh-test-" + Guid.NewGuid().ToString("N");
        using var guard = SingleInstanceGuard.Acquire(Guid.NewGuid().ToString("N"));
        var received = new TaskCompletionSource<string[]>();
        guard.ArgsForwarded += args => received.TrySetResult(args);
        guard.StartListening(pipeName);

        SingleInstanceGuard.SendArgs(pipeName, new[] { "dsh-desktop://session/abc" });
        var args = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "dsh-desktop://session/abc" }, args);
    }
}
