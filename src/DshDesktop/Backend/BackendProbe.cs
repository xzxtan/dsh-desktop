using System.Net;
using System.Net.Http;

namespace DshDesktop.Backend;

public enum ProbeResult
{
    /// <summary>连不上或非 200：后端未就绪。</summary>
    NotReady,
    /// <summary>200 且页面含 Harness 标记：后端就绪。</summary>
    Ready,
    /// <summary>200 但页面不含标记：端口被非 Harness 服务占用。</summary>
    ForeignServer,
}

public interface IBackendProbe
{
    Task<ProbeResult> ProbeAsync(Uri baseUrl, string marker, CancellationToken ct = default);
}

public sealed class HttpBackendProbe : IBackendProbe
{
    private readonly HttpClient _http;
    private readonly int _timeoutMs;

    public HttpBackendProbe(int timeoutMs = 800)
    {
        _timeoutMs = timeoutMs;
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<ProbeResult> ProbeAsync(Uri baseUrl, string marker, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeoutMs);
            using var response = await _http.GetAsync(baseUrl, cts.Token);
            if (response.StatusCode != HttpStatusCode.OK) return ProbeResult.NotReady;
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            return body.Contains(marker, StringComparison.Ordinal) ? ProbeResult.Ready : ProbeResult.ForeignServer;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return ProbeResult.NotReady;
        }
    }
}
