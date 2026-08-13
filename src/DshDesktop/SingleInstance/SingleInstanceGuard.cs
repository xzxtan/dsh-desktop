using System.IO;
using System.IO.Pipes;
using System.Text;

namespace DshDesktop.SingleInstance;

public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultMutexName = @"Local\dsh-desktop-single-instance";
    public const string DefaultPipeName = "dsh-desktop-pipe";

    private readonly Mutex _mutex;
    private readonly bool _isFirst;
    private CancellationTokenSource? _pipeCts;

    public bool IsFirstInstance => _isFirst;

    public event Action<string[]>? ArgsForwarded;

    private SingleInstanceGuard(Mutex mutex, bool isFirst)
    {
        _mutex = mutex;
        _isFirst = isFirst;
    }

    public static SingleInstanceGuard Acquire(string? mutexName = null)
    {
        var mutex = new Mutex(initiallyOwned: true, mutexName ?? DefaultMutexName, out var createdNew);
        return new SingleInstanceGuard(mutex, createdNew);
    }

    public static void ForwardArgsAndExit(string[] args)
    {
        try
        {
            SendArgs(DefaultPipeName, args);
        }
        catch
        {
            // 首实例不存在或管道未就绪：直接退出即可
        }
        Environment.Exit(0);
    }

    public static void SendArgs(string pipeName, string[] args)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
        client.Connect(2000);
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', args));
        client.Write(bytes, 0, bytes.Length);
    }

    public void StartListening(string? pipeName = null)
    {
        _pipeCts?.Cancel();
        _pipeCts = new CancellationTokenSource();
        _ = ListenAsync(pipeName ?? DefaultPipeName, _pipeCts.Token);
    }

    private async Task ListenAsync(string pipeName, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var payload = await reader.ReadToEndAsync(ct);
                var args = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                ArgsForwarded?.Invoke(args);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // 连接中断等：继续监听下一连接
            }
        }
    }

    public void Dispose()
    {
        _pipeCts?.Cancel();
        _pipeCts?.Dispose();
        if (_isFirst)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Dispose 可能在非持有线程上被调用（如异步测试的延续线程）；
                // 此时无法 ReleaseMutex，交由句柄关闭 / 进程退出兜底释放。
            }
        }
        _mutex.Dispose();
    }
}
