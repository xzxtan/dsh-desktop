using DshDesktop.Settings;
using Xunit;

namespace DshDesktop.Tests;

public sealed class SettingsStoreTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dsh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        var store = new SettingsStore(Path.Combine(NewDir(), "settings.json"));

        var s = store.Load();

        Assert.Equal(3080, s.BackendPort);
        Assert.Equal("dsh", s.DshCommand);
        Assert.Equal(new[] { "web" }, s.DshArgs);
        Assert.True(s.CloseToTray);
        Assert.False(s.StopSpawnedBackendOnExit);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var dir = NewDir();
        var store = new SettingsStore(Path.Combine(dir, "settings.json"));
        var original = new AppSettings { BackendPort = 4099, DshCommand = @"C:\tools\dsh.cmd", CloseToTray = false };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(4099, loaded.BackendPort);
        Assert.Equal(@"C:\tools\dsh.cmd", loaded.DshCommand);
        Assert.False(loaded.CloseToTray);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsAndBacksUp()
    {
        var dir = NewDir();
        var file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, "{ 这不是合法 JSON !!");
        var store = new SettingsStore(file);

        var s = store.Load();

        Assert.Equal(3080, s.BackendPort);
        Assert.Single(Directory.GetFiles(dir, "settings.json.corrupt-*"));
    }

    [Fact]
    public void Load_OutOfRangePort_FallsBackToDefault()
    {
        var dir = NewDir();
        var file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, """{ "BackendPort": 99999 }""");
        var store = new SettingsStore(file);

        var s = store.Load();

        Assert.Equal(3080, s.BackendPort);
    }
}
