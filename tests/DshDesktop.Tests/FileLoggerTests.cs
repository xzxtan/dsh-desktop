using DshDesktop.Logging;
using Xunit;

namespace DshDesktop.Tests;

public sealed class FileLoggerTests
{
    [Fact]
    public void Info_WritesTimestampedLine()
    {
        var file = Path.Combine(Path.GetTempPath(), "dsh-tests", Guid.NewGuid().ToString("N"), "app.log");
        var log = new FileLogger(file);

        log.Info("hello");

        var line = File.ReadAllText(file).Trim();
        Assert.Contains("[INFO] hello", line);
    }

    [Fact]
    public void Error_WithException_WritesExceptionText()
    {
        var file = Path.Combine(Path.GetTempPath(), "dsh-tests", Guid.NewGuid().ToString("N"), "app.log");
        var log = new FileLogger(file);

        log.Error("boom", new InvalidOperationException("detail"));

        var text = File.ReadAllText(file);
        Assert.Contains("boom", text);
        Assert.Contains("detail", text);
    }

    [Fact]
    public void Ctor_CreatesMissingDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dsh-tests", Guid.NewGuid().ToString("N"), "nested", "logs");
        var file = Path.Combine(dir, "app.log");

        var log2 = new FileLogger(file);
        log2.Info("x");

        Assert.True(File.Exists(file));
    }
}
