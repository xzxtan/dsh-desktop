namespace DshDesktop.Settings;

public sealed class AppSettings
{
    public int BackendPort { get; set; } = 3080;
    public string DshCommand { get; set; } = "dsh";
    public string[] DshArgs { get; set; } = ["web"];
    public bool StopSpawnedBackendOnExit { get; set; }
    public bool CloseToTray { get; set; } = true;
    public int ReadyTimeoutSeconds { get; set; } = 30;
    public int HealthIntervalSeconds { get; set; } = 5;
    public string PageMarker { get; set; } = "__DSH_BOOT__";
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;

    public Uri BackendBaseUrl => new($"http://127.0.0.1:{BackendPort}/");
}
