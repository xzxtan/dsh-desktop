using System.Net;
using System.Net.Sockets;
using System.Text;
using DshDesktop.Backend;
using Xunit;

namespace DshDesktop.Tests;

public sealed class BackendProbeTests
{
    /// <summary>用临时端口起一个 HttpListener，返回 (listener, baseUrl)。</summary>
    private static async Task<(HttpListener Listener, Uri Url)> StartServerAsync(
        string body, int statusCode = 200)
    {
        // 先占一个临时端口再释放，拿到可用端口号
        var port = FreePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var ctx = await listener.GetContextAsync();
                    var bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.StatusCode = statusCode;
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            }
            catch
            {
                // listener 关闭时正常退出
            }
        });
        return (listener, new Uri($"http://127.0.0.1:{port}/"));
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task Probe_Ready_WhenPageContainsMarker()
    {
        var server = await StartServerAsync("<html><script>window.__DSH_BOOT__=1</script></html>");
        try
        {
            var probe = new HttpBackendProbe();

            var result = await probe.ProbeAsync(server.Url, "__DSH_BOOT__");

            Assert.Equal(ProbeResult.Ready, result);
        }
        finally
        {
            server.Listener.Close();
        }
    }

    [Fact]
    public async Task Probe_ForeignServer_WhenPageLacksMarker()
    {
        var server = await StartServerAsync("<html>nginx default page</html>");
        try
        {
            var probe = new HttpBackendProbe();

            var result = await probe.ProbeAsync(server.Url, "__DSH_BOOT__");

            Assert.Equal(ProbeResult.ForeignServer, result);
        }
        finally
        {
            server.Listener.Close();
        }
    }

    [Fact]
    public async Task Probe_NotReady_WhenNothingListens()
    {
        var port = FreePort();
        var probe = new HttpBackendProbe();

        var result = await probe.ProbeAsync(new Uri($"http://127.0.0.1:{port}/"), "__DSH_BOOT__");

        Assert.Equal(ProbeResult.NotReady, result);
    }

    [Fact]
    public async Task Probe_NotReady_OnNon200()
    {
        var server = await StartServerAsync("boom", statusCode: 500);
        try
        {
            var probe = new HttpBackendProbe();

            var result = await probe.ProbeAsync(server.Url, "__DSH_BOOT__");

            Assert.Equal(ProbeResult.NotReady, result);
        }
        finally
        {
            server.Listener.Close();
        }
    }
}
