# dsh-desktop 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建一个 Windows 桌面薄壳 dsh-desktop：WPF + WebView2 打开 DeepSeek Harness 现有 Web UI（`http://127.0.0.1:3080`），自动拉起/连接后端，带单实例、托盘与深链。

**Architecture:** 壳是纯客户端——页面与后端之间保持现状（HTTP 上行 + WebSocket 下行）；壳与后端之间只有「就绪/健康探测」与「自己拉起进程的生命周期控制」两类交互。核心可测逻辑（设置、探测、状态机、深链解析）与 UI 分离，全部 TDD。

**Tech Stack:** .NET 10（net10.0-windows）、WPF、Microsoft.Web.WebView2、Hardcodet.NotifyIcon.Wpf、xUnit、Microsoft.Extensions.TimeProvider.Testing（FakeTimeProvider）。

**设计文档（spec）：** `docs/superpowers/specs/2026-08-13-dsh-desktop-design.md`——所有任务的需求依据，任务内不再重复论证。

## Global Constraints

- 目标框架：`net10.0-windows`（环境实测 SDK 10.0.301 / WindowsDesktop 10.0.9）。
- 仓库根：`D:\DeepSeek Harness\dsh-desktop`（独立 git 仓库；**不要**碰仓库外的 `..\node_modules`）。
- 后端默认端口 3080；健康检查内容标记 `__DSH_BOOT__`（已在实页验证）；页面标题 `DeepSeek Harness`。
- 不改动 Harness 后端任何代码；壳不代理、不缓存会话状态。
- 拉起后端：`cmd.exe /c dsh <args>`（PATH 上已有 `dsh.cmd` shim）；停止自己拉起的后端用 `taskkill /PID <pid> /T /F`；**绝不**终止非自己拉起的后端（attach 路径）。
- UI 文案用中文；命名空间根 `DshDesktop`；C# 12+（collection expression、file-scoped namespace 可用）。
- 每任务以可独立验证的交付物结束，每个任务末尾提交（提交信息 `feat:` / `test:` / `docs:` 前缀）。
- 手动验证中**不得**杀掉当前 3080 上正在运行的会话后端（那是正在使用中的 Harness）；spawn 路径的验证放到 Task 9 最终清单，由用户主动退出该后端后进行。
- WPF 项目（`UseWPF=true`）**不含** `System.IO` 与 `System.Net.Http` 隐式 using（WindowsDesktop SDK 为避免与 `System.Windows.Shapes.Path` 等冲突而剔除）：源码中用到 `Path`/`File`/`Stream`/`TextWriter` 的文件必须显式 `using System.IO;`，用到 `HttpClient`/`HttpRequestException` 的文件必须显式 `using System.Net.Http;`。
- 本计划的测试 csproj 未启用 `<Using Include="Xunit" />`：所有测试文件必须显式 `using Xunit;`。
- `AppSettings` 的 `WindowLeft`/`WindowTop` 默认为 `double.NaN`：序列化需 `JsonNumberHandling.AllowNamedFloatingPointLiterals`。
- WPF WebView2 有 airspace 限制：WPF 覆盖层无法渲染在 WebView2 之上，离线覆盖层采用「折叠 WebView2 + 全窗覆盖层」方案。

## File Structure

```
dsh-desktop/
├─ .gitignore
├─ DshDesktop.sln
├─ src/DshDesktop/
│  ├─ DshDesktop.csproj              # WPF, net10.0-windows
│  ├─ App.xaml / App.xaml.cs         # 启动编排：设置→单实例→后端→窗口→托盘→深链
│  ├─ MainWindow.xaml / .xaml.cs     # WebView2 宿主 + 离线覆盖层
│  ├─ SettingsWindow.xaml / .xaml.cs # 极简设置对话框（Task 9）
│  ├─ AppPaths.cs                    # 数据/日志/设置路径
│  ├─ Settings/AppSettings.cs        # 设置模型 + 默认值
│  ├─ Settings/SettingsStore.cs      # JSON 读写、损坏恢复
│  ├─ Logging/FileLogger.cs          # 壳日志
│  ├─ Backend/BackendProbe.cs        # ProbeResult / IBackendProbe / HttpBackendProbe
│  ├─ Backend/BackendManager.cs      # 状态机 + 健康监控（核心）
│  ├─ Backend/ProcessRunner.cs       # 真实进程实现（cmd /c、taskkill /T）
│  ├─ SingleInstance/SingleInstanceGuard.cs
│  ├─ Tray/TrayService.cs            # 托盘 + 菜单
│  ├─ Tray/TrayIcons.cs              # 三态 16x16 图标（运行时生成）
│  ├─ DeepLink/DeepLinkParser.cs     # dsh-desktop:// 解析（纯函数，可测）
│  ├─ DeepLink/DeepLinkRegistrar.cs  # HKCU 注册表注册
│  └─ publish.ps1
├─ tests/DshDesktop.Tests/
│  ├─ DshDesktop.Tests.csproj        # net10.0-windows, xUnit, 引用主项目
│  ├─ SettingsStoreTests.cs
│  ├─ FileLoggerTests.cs
│  ├─ BackendProbeTests.cs
│  ├─ BackendManagerTests.cs
│  ├─ SingleInstanceGuardTests.cs
│  └─ DeepLinkParserTests.cs
├─ README.md                         # Task 9
└─ docs/superpowers/specs/2026-08-13-dsh-desktop-design.md
```

---

### Task 1: 仓库与解决方案脚手架

**Files:**
- Create: `.gitignore`、`DshDesktop.sln`、`src/DshDesktop/DshDesktop.csproj`、`src/DshDesktop/App.xaml`、`src/DshDesktop/App.xaml.cs`、`src/DshDesktop/MainWindow.xaml`、`src/DshDesktop/MainWindow.xaml.cs`（后四个由模板生成，本任务只验收不重写）

**Interfaces:**
- Consumes: 无
- Produces: 可构建的 WPF 解决方案；git 仓库（提交了 spec 与计划文档）

- [ ] **Step 1: 初始化 git 仓库**

```powershell
cd "D:\DeepSeek Harness\dsh-desktop"
git init -b main
```

- [ ] **Step 2: 写 .gitignore**

创建 `.gitignore`：

```gitignore
bin/
obj/
*.user
.vs/
dist/
```

- [ ] **Step 3: 生成解决方案与 WPF 项目**

```powershell
dotnet new sln -n DshDesktop
dotnet new wpf -n DshDesktop -o src/DshDesktop
dotnet sln add src/DshDesktop/DshDesktop.csproj
```

- [ ] **Step 4: 核对 csproj 与规范一致**

读取 `src/DshDesktop/DshDesktop.csproj`，确认内容为（模板输出应一致，如有缺项补上）：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

</Project>
```

- [ ] **Step 5: 构建**

```powershell
dotnet build
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: 冒烟运行**

```powershell
dotnet run --project src/DshDesktop
```

Expected: 弹出标题为 `MainWindow` 的空窗口，关闭后进程正常退出（无异常输出）。这是模板默认内容，Task 5 会整体替换。

- [ ] **Step 7: 提交**

```powershell
git add .gitignore DshDesktop.sln src docs
git commit -m "chore: scaffold WPF solution and commit design docs"
```

Expected: 提交成功；`git log --oneline` 显示 1 条提交。

---

### Task 2: 设置、路径与日志（TDD）

**Files:**
- Create: `src/DshDesktop/AppPaths.cs`、`src/DshDesktop/Settings/AppSettings.cs`、`src/DshDesktop/Settings/SettingsStore.cs`、`src/DshDesktop/Logging/FileLogger.cs`
- Create: `tests/DshDesktop.Tests/DshDesktop.Tests.csproj`、`tests/DshDesktop.Tests/SettingsStoreTests.cs`、`tests/DshDesktop.Tests/FileLoggerTests.cs`
- Modify: `DshDesktop.sln`（加入测试项目）

**Interfaces:**
- Produces:
  - `AppPaths.AppDataDir`、`AppPaths.LogsDir`、`AppPaths.SettingsFile`、`AppPaths.WebView2UserDataDir`（均为 `string`）
  - `AppSettings`（见下，Task 4/5 消费）
  - `SettingsStore(string filePath)`：`AppSettings Load()`、`void Save(AppSettings)`
  - `FileLogger(string filePath)`：`void Info(string)`、`void Warn(string)`、`void Error(string, Exception? = null)`

- [ ] **Step 1: 创建测试项目并加入解决方案**

```powershell
dotnet new xunit -n DshDesktop.Tests -o tests/DshDesktop.Tests
dotnet sln add tests/DshDesktop.Tests/DshDesktop.Tests.csproj
```

- [ ] **Step 2: 重写测试项目 csproj**

用以下内容完整覆盖 `tests/DshDesktop.Tests/DshDesktop.Tests.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\DshDesktop\DshDesktop.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: 写失败的测试**

创建 `tests/DshDesktop.Tests/SettingsStoreTests.cs`：

```csharp
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
```

创建 `tests/DshDesktop.Tests/FileLoggerTests.cs`：

```csharp
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
```

- [ ] **Step 4: 运行测试确认失败**

```powershell
dotnet test tests/DshDesktop.Tests
```

Expected: FAIL，编译错误（`DshDesktop.Settings`、`DshDesktop.Logging` 类型不存在）。

- [ ] **Step 5: 实现 AppPaths**

创建 `src/DshDesktop/AppPaths.cs`：

```csharp
using System.IO;

namespace DshDesktop;

public static class AppPaths
{
    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "dsh-desktop");

    public static string LogsDir => Path.Combine(AppDataDir, "logs");

    public static string SettingsFile => Path.Combine(AppDataDir, "settings.json");

    public static string WebView2UserDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "dsh-desktop", "WebView2");
}
```

- [ ] **Step 6: 实现 AppSettings**

创建 `src/DshDesktop/Settings/AppSettings.cs`：

```csharp
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
```

- [ ] **Step 7: 实现 SettingsStore**

创建 `src/DshDesktop/Settings/SettingsStore.cs`：

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DshDesktop.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string FilePath { get; }

    public SettingsStore(string filePath) => FilePath = filePath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
            if (loaded is null) return new AppSettings();
            Normalize(loaded);
            return loaded;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            BackupCorruptFile();
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }

    private void BackupCorruptFile()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Copy(FilePath, FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
        }
        catch
        {
            // best-effort
        }
    }

    private static void Normalize(AppSettings s)
    {
        s.BackendPort = s.BackendPort is >= 1 and <= 65535 ? s.BackendPort : 3080;
        s.ReadyTimeoutSeconds = Math.Clamp(s.ReadyTimeoutSeconds, 1, 300);
        s.HealthIntervalSeconds = Math.Clamp(s.HealthIntervalSeconds, 1, 300);
        s.DshCommand = string.IsNullOrWhiteSpace(s.DshCommand) ? "dsh" : s.DshCommand;
        s.PageMarker = string.IsNullOrEmpty(s.PageMarker) ? "__DSH_BOOT__" : s.PageMarker;
        s.DshArgs ??= ["web"];
    }
}
```

- [ ] **Step 8: 实现 FileLogger**

创建 `src/DshDesktop/Logging/FileLogger.cs`：

```csharp
using System.IO;

namespace DshDesktop.Logging;

public sealed class FileLogger
{
    private readonly object _gate = new();
    private readonly string _filePath;

    public FileLogger(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} {ex}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                File.AppendAllText(_filePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志绝不能拖垮壳
        }
    }
}
```

- [ ] **Step 9: 运行测试确认通过**

```powershell
dotnet test tests/DshDesktop.Tests
```

Expected: PASS，7 个测试全绿（`Passed: 7`）。

- [ ] **Step 10: 提交**

```powershell
git add -A
git commit -m "feat: settings store, app paths and file logger"
```

---

### Task 3: 后端探测（TDD）

**Files:**
- Create: `src/DshDesktop/Backend/BackendProbe.cs`、`tests/DshDesktop.Tests/BackendProbeTests.cs`

**Interfaces:**
- Consumes: `AppSettings`（仅类型上不依赖，探测函数直接收 `Uri` 与 `marker`）
- Produces:
  - `enum ProbeResult { NotReady, Ready, ForeignServer }`
  - `interface IBackendProbe { Task<ProbeResult> ProbeAsync(Uri baseUrl, string marker, CancellationToken ct = default); }`
  - `class HttpBackendProbe(int timeoutMs = 800) : IBackendProbe`
  - Task 4 的 `BackendManager` 依赖这些签名。

- [ ] **Step 1: 写失败的测试**

创建 `tests/DshDesktop.Tests/BackendProbeTests.cs`：

```csharp
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
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test tests/DshDesktop.Tests
```

Expected: FAIL，`ProbeResult`、`HttpBackendProbe` 不存在。

- [ ] **Step 3: 实现 BackendProbe**

创建 `src/DshDesktop/Backend/BackendProbe.cs`：

```csharp
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
```

- [ ] **Step 4: 运行测试确认通过**

```powershell
dotnet test tests/DshDesktop.Tests
```

Expected: PASS（`Passed: 11`）。

- [ ] **Step 5: 提交**

```powershell
git add -A
git commit -m "feat: backend probe with ready/foreign/not-ready classification"
```

---

### Task 4: BackendManager 状态机与进程运行器（TDD）

**Files:**
- Create: `src/DshDesktop/Backend/BackendManager.cs`、`src/DshDesktop/Backend/ProcessRunner.cs`、`tests/DshDesktop.Tests/BackendManagerTests.cs`
- Modify: `tests/DshDesktop.Tests/DshDesktop.Tests.csproj`（加 `Microsoft.Extensions.TimeProvider.Testing`）

**Interfaces:**
- Consumes: `ProbeResult` / `IBackendProbe`（Task 3）、`AppSettings` / `FileLogger`（Task 2）
- Produces（Task 5/7 消费）:
  - `enum BackendState { Idle, Attached, Spawning, WaitingReady, Online, Offline, Failed }`
  - `interface IProcessRunner { int Start(string fileName, string arguments, TextWriter output); void Stop(int processId); bool IsRunning(int processId); }`
  - `class BackendManager : IDisposable`：构造 `(AppSettings, IBackendProbe, IProcessRunner, FileLogger, TimeProvider? time = null)`；`BackendState State`；`int? OwnedProcessId`；`bool OwnsBackend`；`Uri BaseUrl`；`event Action<BackendState>? StateChanged`；`Task<bool> EnsureStartedAsync(CancellationToken ct = default)`；`Task<bool> RetryAsync(CancellationToken ct = default)`；`void StopOwnedBackend()`
  - `class ProcessRunner : IProcessRunner`（真实实现，手动验证）

- [ ] **Step 1: 加 TimeProvider.Testing 包**

```powershell
dotnet add tests/DshDesktop.Tests package Microsoft.Extensions.TimeProvider.Testing
```

- [ ] **Step 2: 写失败的测试**

创建 `tests/DshDesktop.Tests/BackendManagerTests.cs`：

```csharp
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
    public void StopOwnedBackend_NoOp_WhenAttachedOnly()
    {
        var (manager, probe, runner, _) = NewManager();
        probe.Results.Enqueue(ProbeResult.Ready);
        manager.EnsureStartedAsync().GetAwaiter().GetResult();

        manager.StopOwnedBackend();

        Assert.Empty(runner.Stopped);
    }
}
```

- [ ] **Step 3: 运行测试确认失败**

```powershell
dotnet test tests/DshDesktop.Tests
```

Expected: FAIL，`BackendManager` 不存在。

- [ ] **Step 4: 实现 BackendManager**

创建 `src/DshDesktop/Backend/BackendManager.cs`：

```csharp
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
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.HealthIntervalSeconds), _time, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

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
```

- [ ] **Step 5: 实现 ProcessRunner**

创建 `src/DshDesktop/Backend/ProcessRunner.cs`：

```csharp
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
```

- [ ] **Step 6: 运行测试确认通过**

```powershell
dotnet test tests/DshDesktop.Tests
```

Expected: PASS（`Passed: 18`）。

- [ ] **Step 7: 提交**

```powershell
git add -A
git commit -m "feat: backend lifecycle state machine with health monitoring"
```

---

### Task 5: 主窗口 WebView2 壳 + 应用启动编排

**Files:**
- Modify: `src/DshDesktop/DshDesktop.csproj`（加 WebView2 包）
- Replace: `src/DshDesktop/MainWindow.xaml`、`src/DshDesktop/MainWindow.xaml.cs`、`src/DshDesktop/App.xaml`、`src/DshDesktop/App.xaml.cs`

**Interfaces:**
- Consumes: `BackendManager` / `BackendState`（Task 4）、`AppSettings` / `SettingsStore` / `FileLogger` / `AppPaths`（Task 2）
- Produces: 可用的桌面壳（attach 当前 3080 后端即可用）；Task 6/7/8 在 `App.xaml.cs` 上增量添加单实例/托盘/深链。

- [ ] **Step 1: 加 WebView2 NuGet 包**

```powershell
dotnet add src/DshDesktop package Microsoft.Web.WebView2
```

核对 `src/DshDesktop/DshDesktop.csproj` 出现：

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.Web.WebView2" Version="..." />
  </ItemGroup>
```

- [ ] **Step 2: 重写 App.xaml**

用以下内容完整覆盖 `src/DshDesktop/App.xaml`（注意：无 `StartupUri`，由代码编排启动）：

```xml
<Application x:Class="DshDesktop.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
</Application>
```

- [ ] **Step 3: 重写 App.xaml.cs**

用以下内容完整覆盖 `src/DshDesktop/App.xaml.cs`：

```csharp
using System.Windows;
using DshDesktop.Backend;
using DshDesktop.Logging;
using DshDesktop.Settings;

namespace DshDesktop;

public partial class App : Application
{
    public static bool IsExiting { get; private set; }
    public static FileLogger Log = null!;
    public static SettingsStore SettingsStore = null!;
    public static AppSettings Settings = null!;
    public static BackendManager Backend = null!;

    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log = new FileLogger(System.IO.Path.Combine(AppPaths.LogsDir, "dsh-desktop.log"));
        SettingsStore = new SettingsStore(AppPaths.SettingsFile);
        Settings = SettingsStore.Load();
        Log.Info($"启动 dsh-desktop，参数: {string.Join(' ', e.Args)}");

        Backend = new BackendManager(Settings, new HttpBackendProbe(), new ProcessRunner(), Log);
        Backend.StateChanged += state => Log.Info($"后端状态: {state}");

        _mainWindow = new MainWindow(Backend);
        MainWindow = _mainWindow;
        _mainWindow.Show();
        await _mainWindow.InitAsync();

        var started = await Backend.EnsureStartedAsync();
        Log.Info(started ? "后端就绪" : "后端未就绪（离线覆盖层）");
    }

    public static void RequestExit()
    {
        IsExiting = true;
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        if (Settings.StopSpawnedBackendOnExit)
            Backend.StopOwnedBackend();
        Backend.Dispose();
        Log.Info("退出");
        base.OnExit(e);
    }
}
```

- [ ] **Step 4: 重写 MainWindow.xaml**

用以下内容完整覆盖 `src/DshDesktop/MainWindow.xaml`。设计说明：WPF WebView2 是 HWND 子窗口，有 airspace 限制——WPF 元素无法叠在其上渲染，因此离线时**折叠 WebView2**、覆盖层独占全窗；在线时反之。

```xml
<Window x:Class="DshDesktop.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
        Title="DeepSeek Harness"
        Width="1280" Height="800"
        WindowStartupLocation="CenterScreen">
    <Grid>
        <wv2:WebView2 x:Name="Browser" Visibility="Collapsed" />
        <Border x:Name="Overlay" Background="#F0141826">
            <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
                <TextBlock x:Name="OverlayTitle" Text="正在启动 DeepSeek Harness…"
                           FontSize="20" Foreground="White" HorizontalAlignment="Center" />
                <TextBlock x:Name="OverlayDetail" Text="正在连接后端…"
                           Foreground="#B0FFFFFF" HorizontalAlignment="Center" Margin="0,8,0,0" />
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,20,0,0">
                    <Button x:Name="RetryButton" Content="重试" Width="96" Margin="0,0,8,0"
                            Visibility="Collapsed" Click="Retry_Click" />
                    <Button Content="打开设置" Width="96" Click="Settings_Click" />
                </StackPanel>
            </StackPanel>
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 5: 重写 MainWindow.xaml.cs**

用以下内容完整覆盖 `src/DshDesktop/MainWindow.xaml.cs`：

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using DshDesktop.Backend;
using Microsoft.Web.WebView2.Core;

namespace DshDesktop;

public partial class MainWindow : Window
{
    private readonly BackendManager _backend;
    private bool _navigatedOnce;

    public MainWindow(BackendManager backend)
    {
        _backend = backend;
        InitializeComponent();
        _backend.StateChanged += OnBackendStateChanged;
    }

    public async Task InitAsync()
    {
        var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebView2UserDataDir);
        await Browser.EnsureCoreWebView2Async(env);
        Browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
        Browser.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
            }
            catch
            {
                // 忽略打不开的链接
            }
        };
        Browser.NavigationCompleted += Browser_NavigationCompleted;

        if (_backend.State == BackendState.Online)
            NavigateToBackend();
        else
            ShowOverlay("正在启动 DeepSeek Harness…", "正在连接后端…", showRetry: false);
    }

    private void NavigateToBackend()
    {
        var url = _backend.BaseUrl;
        if (Browser.CoreWebView2 is not null && Browser.CoreWebView2.Source != url)
            Browser.CoreWebView2.Navigate(url.ToString());
        _navigatedOnce = true;
        HideOverlay();
    }

    private void OnBackendStateChanged(BackendState state)
    {
        Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case BackendState.Spawning:
                case BackendState.WaitingReady:
                    ShowOverlay("正在启动 DeepSeek Harness…", "后端未运行，正在自动拉起 dsh web", showRetry: false);
                    break;
                case BackendState.Online:
                    if (Browser.CoreWebView2 is not null)
                        NavigateToBackend();
                    else
                        _navigatedOnce = false; // 等 InitAsync 完成后导航
                    HideOverlay();
                    break;
                case BackendState.Offline:
                    ShowOverlay("后端未连接", "DeepSeek Harness 已停止。点击重试重新连接。", showRetry: true);
                    break;
                case BackendState.Failed:
                    ShowOverlay("启动失败", "无法启动 dsh web。可在设置中配置 dsh 路径后重试。", showRetry: true);
                    break;
            }
        });
    }

    private void Browser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
            ShowOverlay("页面加载失败", "后端可能已离线，点击重试。", showRetry: true);
    }

    private void ShowOverlay(string title, string detail, bool showRetry)
    {
        OverlayTitle.Text = title;
        OverlayDetail.Text = detail;
        RetryButton.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
        Overlay.Visibility = Visibility.Visible;
        Browser.Visibility = Visibility.Collapsed;
    }

    private void HideOverlay()
    {
        Overlay.Visibility = Visibility.Collapsed;
        Browser.Visibility = Visibility.Visible;
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        ShowOverlay("正在重连…", "正在重新连接后端…", showRetry: false);
        var ok = await _backend.RetryAsync();
        if (ok && Browser.CoreWebView2 is not null)
            NavigateToBackend();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // Task 9 引入设置对话框；当前阶段直接打开 settings.json 所在目录提示
        MessageBox.Show(
            $"设置文件: {AppPaths.SettingsFile}\n\n（设置对话框将在后续任务提供）",
            "设置", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F12 && Browser.CoreWebView2 is not null)
            Browser.CoreWebView2.OpenDevToolsWindow();
        else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.Control)
            Browser.CoreWebView2?.Reload();
        base.OnKeyDown(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (App.IsExiting) return;
        if (App.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            App.RequestExit(); // 直接退出整个应用
        }
        base.OnClosing(e);
    }
}
```

注意：`Settings_Click` 中的占位实现（消息框）会在 Task 9 替换为真正的设置对话框；这是刻意的阶段性实现，不是计划占位符。

- [ ] **Step 6: 构建**

```powershell
dotnet build
```

Expected: `Build succeeded`。

- [ ] **Step 7: 手动验证 attach 路径**

当前 3080 上正跑着会话后端（**不要停它**）：

```powershell
dotnet run --project src/DshDesktop
```

Expected 依次出现：
1. 窗口弹出，覆盖层短暂显示「正在启动…」；
2. 覆盖层消失，WebView2 加载出 DeepSeek Harness Web UI（标题栏显示 `DeepSeek Harness`），页面可正常对话/浏览；
3. 按 `F12` 弹出 DevTools，`Ctrl+F5` 刷新，`Ctrl+滚轮` 缩放；
4. 点击页面里的外链 → 在系统默认浏览器打开，壳内不开新窗；
5. 关闭窗口 → 窗口隐藏（closeToTray 默认 true）。**用任务管理器结束 `DshDesktop` 进程**（托盘退出功能 Task 7 才提供），确认结束；
6. 查看 `%APPDATA%\dsh-desktop\logs\dsh-desktop.log`，应出现「后端状态: Attached」「后端状态: Online」等记录。

- [ ] **Step 8: 提交**

```powershell
git add -A
git commit -m "feat: webview2 shell window with offline overlay and startup wiring"
```

---

### Task 6: 单实例锁与参数转发

**Files:**
- Create: `src/DshDesktop/SingleInstance/SingleInstanceGuard.cs`、`tests/DshDesktop.Tests/SingleInstanceGuardTests.cs`
- Modify: `src/DshDesktop/App.xaml.cs`（接入 guard）

**Interfaces:**
- Consumes: 无
- Produces（Task 8 复用）:
  - `class SingleInstanceGuard : IDisposable`：`bool IsFirstInstance`；`static SingleInstanceGuard Acquire()`；`static void ForwardArgsAndExit(string[] args)`；`static void SendArgs(string pipeName, string[] args)`；`event Action<string[]>? ArgsForwarded`；`void StartListening()`

- [ ] **Step 1: 写失败的测试**

创建 `tests/DshDesktop.Tests/SingleInstanceGuardTests.cs`：

```csharp
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
```

注意：`Acquire` 与 `StartListening` 在 Task 4 版实现中不带参数（用常量名）；本测试调用的是带参数的重载/可选参数形态。实现步骤中的签名必须与测试一致——`Acquire(string? mutexName = null)`、`StartListening(string? pipeName = null)`。

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test tests/DshDesktop.Tests
```

Expected: FAIL，`SingleInstanceGuard` 不存在。

- [ ] **Step 3: 实现 SingleInstanceGuard**

创建 `src/DshDesktop/SingleInstance/SingleInstanceGuard.cs`：

```csharp
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
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```powershell
dotnet test tests/DshDesktop.Tests
```

Expected: PASS（`Passed: 21`）。

- [ ] **Step 5: 接入 App.xaml.cs**

修改 `src/DshDesktop/App.xaml.cs`：

1. `using DshDesktop.SingleInstance;` 加入 using 区；
2. 字段区加 `private SingleInstanceGuard? _guard;`
3. `OnStartup` 中，在 `Log.Info($"启动 dsh-desktop…")` 之后、创建 `Backend` 之前插入：

```csharp
        _guard = SingleInstanceGuard.Acquire();
        if (!_guard.IsFirstInstance)
        {
            SingleInstanceGuard.ForwardArgsAndExit(e.Args);
            Shutdown();
            return;
        }
```

4. `OnExit` 中，`Backend.Dispose();` 之后加：

```csharp
        _guard?.Dispose();
```

- [ ] **Step 6: 构建 + 手动验证**

```powershell
dotnet build
dotnet run --project src/DshDesktop
```

保持第一个实例运行，另开 PowerShell：

```powershell
dotnet run --project src/DshDesktop
```

Expected: 第二个进程秒退（无新窗口）；第一个实例的日志出现「启动 dsh-desktop」只有一条（第二次启动未进入主流程）。

- [ ] **Step 7: 提交**

```powershell
git add -A
git commit -m "feat: single-instance guard with named-pipe arg forwarding"
```

---

### Task 7: 系统托盘

**Files:**
- Create: `src/DshDesktop/Tray/TrayIcons.cs`、`src/DshDesktop/Tray/TrayService.cs`
- Modify: `src/DshDesktop/DshDesktop.csproj`（加 Hardcodet.NotifyIcon.Wpf）、`src/DshDesktop/App.xaml.cs`（接入托盘）

**Interfaces:**
- Consumes: `BackendManager` / `BackendState`（Task 4）
- Produces: `class TrayService : IDisposable`：构造 `(BackendManager backend, Window window)`；`void Initialize()`

- [ ] **Step 1: 加托盘 NuGet 包**

```powershell
dotnet add src/DshDesktop package Hardcodet.NotifyIcon.Wpf
```

- [ ] **Step 2: 实现 TrayIcons（三态 16x16 图标，运行时生成）**

创建 `src/DshDesktop/Tray/TrayIcons.cs`：

```csharp
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DshDesktop.Tray;

public static class TrayIcons
{
    public static ImageSource Online { get; } = Make(0xFF2E7D32);
    public static ImageSource Offline { get; } = Make(0xFFC62828);
    public static ImageSource Starting { get; } = Make(0xFF9E9E9E);

    private static ImageSource Make(uint argb)
    {
        const int size = 16;
        var a = (byte)(argb >> 24);
        var r = (byte)(argb >> 16);
        var g = (byte)(argb >> 8);
        var b = (byte)argb;
        var value = (uint)((a << 24) | (r << 16) | (g << 8) | b); // BGRA 内存序
        var pixels = new uint[size * size];
        Array.Fill(pixels, value);
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bmp.Freeze();
        return bmp;
    }
}
```

- [ ] **Step 3: 实现 TrayService**

创建 `src/DshDesktop/Tray/TrayService.cs`：

```csharp
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DshDesktop.Backend;
using Hardcodet.Wpf.TaskbarNotification;

namespace DshDesktop.Tray;

public sealed class TrayService : IDisposable
{
    private readonly BackendManager _backend;
    private readonly Window _window;
    private readonly TaskbarIcon _icon = new();
    private MenuItem _stateItem = null!;
    private MenuItem _restartItem = null!;

    public TrayService(BackendManager backend, Window window)
    {
        _backend = backend;
        _window = window;
        _backend.StateChanged += _ => Update();
    }

    public void Initialize()
    {
        _icon.ToolTipText = "DeepSeek Harness";
        _icon.IconSource = TrayIcons.Starting;

        var menu = new ContextMenu();
        var show = new MenuItem { Header = "显示主窗口" };
        show.Click += (_, _) => ShowWindow();
        var openBrowser = new MenuItem { Header = "在浏览器中打开" };
        openBrowser.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(_backend.BaseUrl.ToString()) { UseShellExecute = true });
            }
            catch { /* 忽略 */ }
        };
        _stateItem = new MenuItem { Header = "状态：…", IsEnabled = false };
        _restartItem = new MenuItem { Header = "重启后端" };
        _restartItem.Click += async (_, _) => await _backend.RetryAsync();
        var settings = new MenuItem { Header = "设置" };
        settings.Click += (_, _) =>
            MessageBox.Show($"设置文件: {AppPaths.SettingsFile}", "设置",
                MessageBoxButton.OK, MessageBoxImage.Information);
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => App.RequestExit();

        foreach (var item in new object[]
                 {
                     show, openBrowser, new Separator(), _stateItem, _restartItem, settings,
                     new Separator(), exit,
                 })
            menu.Items.Add(item);

        _icon.ContextMenu = menu;
        _icon.TrayMouseDoubleClick += (_, _) => ShowWindow();
        Update();
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Update()
    {
        if (!_window.Dispatcher.CheckAccess())
        {
            _window.Dispatcher.Invoke(Update);
            return;
        }

        _stateItem.Header = _backend.State switch
        {
            BackendState.Online => "状态：后端在线",
            BackendState.Offline => "状态：后端离线",
            BackendState.Failed => "状态：启动失败",
            _ => "状态：连接中…",
        };
        _restartItem.IsEnabled = _backend.OwnsBackend;
        _icon.IconSource = _backend.State switch
        {
            BackendState.Online => TrayIcons.Online,
            BackendState.Offline or BackendState.Failed => TrayIcons.Offline,
            _ => TrayIcons.Starting,
        };
        _icon.ToolTipText = $"DeepSeek Harness — {_stateItem.Header}";
    }

    public void Dispose() => _icon.Dispose();
}
```

（托盘「设置」菜单项与 Task 5 的 `Settings_Click` 一样，Task 9 会替换为真正的设置对话框。）

- [ ] **Step 4: 接入 App.xaml.cs**

修改 `src/DshDesktop/App.xaml.cs`：

1. using 区加 `using DshDesktop.Tray;`
2. 字段区加 `private TrayService? _tray;`
3. `OnStartup` 末尾（`Log.Info(started ? ...)` 之后）加：

```csharp
        _tray = new TrayService(Backend, _mainWindow);
        _tray.Initialize();
```

4. `OnExit` 中，`Backend.Dispose();` 之后加：

```csharp
        _tray?.Dispose();
```

- [ ] **Step 5: 构建 + 手动验证**

```powershell
dotnet build
dotnet run --project src/DshDesktop
```

Expected：
1. 托盘出现图标（后端在线 → 绿色）；
2. 右键菜单：显示主窗口 / 在浏览器中打开 / 状态：后端在线（灰）/ 重启后端（**灰**，attach 模式不可用）/ 设置 / 退出；
3. 双击托盘图标 → 窗口恢复；
4. 关闭窗口 → 进托盘；托盘「显示主窗口」→ 恢复；
5. 「在浏览器中打开」→ 默认浏览器打开 `http://127.0.0.1:3080/`；
6. 托盘「退出」→ 应用真正退出、托盘图标消失、进程结束；日志出现「退出」。

- [ ] **Step 6: 提交**

```powershell
git add -A
git commit -m "feat: system tray with live backend status and menu"
```

---

### Task 8: 深链协议（TDD 解析 + 注册表注册）

**Files:**
- Create: `src/DshDesktop/DeepLink/DeepLinkParser.cs`、`src/DshDesktop/DeepLink/DeepLinkRegistrar.cs`、`tests/DshDesktop.Tests/DeepLinkParserTests.cs`
- Modify: `src/DshDesktop/App.xaml.cs`（注册协议 + 转发参数路由）

**Interfaces:**
- Consumes: `SingleInstanceGuard`（Task 6，`ArgsForwarded` 事件）
- Produces:
  - `enum DeepLinkAction { Launch, Session }`
  - `record DeepLinkRequest(DeepLinkAction Action, string? SessionId)`
  - `static bool DeepLinkRequest.TryParse(string arg, out DeepLinkRequest request)`
  - `static class DeepLinkRegistrar`：`void Register(string exePath)`、`string? GetRegisteredCommand()`

- [ ] **Step 1: 写失败的测试**

创建 `tests/DshDesktop.Tests/DeepLinkParserTests.cs`：

```csharp
using DshDesktop.DeepLink;
using Xunit;

namespace DshDesktop.Tests;

public sealed class DeepLinkParserTests
{
    [Theory]
    [InlineData("dsh-desktop://")]
    [InlineData("dsh-desktop:")]
    [InlineData("dsh-desktop://launch")]
    [InlineData("DSH-DESKTOP://LAUNCH")]
    public void Parse_LaunchForms_ReturnLaunch(string arg)
    {
        var ok = DeepLinkRequest.TryParse(arg, out var request);

        Assert.True(ok);
        Assert.Equal(DeepLinkAction.Launch, request.Action);
        Assert.Null(request.SessionId);
    }

    [Fact]
    public void Parse_SessionForm_ReturnsSessionId()
    {
        var ok = DeepLinkRequest.TryParse("dsh-desktop://session/sess-42", out var request);

        Assert.True(ok);
        Assert.Equal(DeepLinkAction.Session, request.Action);
        Assert.Equal("sess-42", request.SessionId);
    }

    [Fact]
    public void Parse_SessionWithoutId_Fails()
    {
        Assert.False(DeepLinkRequest.TryParse("dsh-desktop://session/", out _));
        Assert.False(DeepLinkRequest.TryParse("dsh-desktop://session", out _));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dsh-desktop://unknown")]
    public void Parse_InvalidArgs_Fail(string arg)
    {
        Assert.False(DeepLinkRequest.TryParse(arg, out _));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test tests/DshDesktop.Tests
```

Expected: FAIL，`DeepLinkRequest` 不存在。

- [ ] **Step 3: 实现 DeepLinkParser**

创建 `src/DshDesktop/DeepLink/DeepLinkParser.cs`：

```csharp
namespace DshDesktop.DeepLink;

public enum DeepLinkAction
{
    Launch,
    Session,
}

public sealed record DeepLinkRequest(DeepLinkAction Action, string? SessionId)
{
    public static bool TryParse(string arg, out DeepLinkRequest request)
    {
        request = new DeepLinkRequest(DeepLinkAction.Launch, null);
        if (string.IsNullOrWhiteSpace(arg)) return false;

        var rest = arg.Trim();
        const string prefix = "dsh-desktop:";
        if (!rest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        rest = rest[prefix.Length..].TrimStart('/');

        if (rest.Length == 0) return true; // 裸 dsh-desktop: / dsh-desktop://
        if (rest.StartsWith("session/", StringComparison.OrdinalIgnoreCase))
        {
            var id = rest["session/".Length..];
            if (id.Length == 0) return false;
            request = new DeepLinkRequest(DeepLinkAction.Session, id);
            return true;
        }
        return rest.StartsWith("launch", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```powershell
dotnet test tests/DshDesktop.Tests
```

Expected: PASS（`Passed: 31`）。

- [ ] **Step 5: 实现 DeepLinkRegistrar**

创建 `src/DshDesktop/DeepLink/DeepLinkRegistrar.cs`：

```csharp
using Microsoft.Win32;

namespace DshDesktop.DeepLink;

public static class DeepLinkRegistrar
{
    public const string Scheme = "dsh-desktop";

    public static void Register(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
        key.SetValue(null, "URL:DeepSeek Harness Desktop");
        key.SetValue("URL Protocol", "");
        using var cmd = key.CreateSubKey(@"shell\open\command");
        cmd.SetValue(null, $"\"{exePath}\" \"%1\"");
    }

    public static string? GetRegisteredCommand()
    {
        using var cmd = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Scheme}\shell\open\command");
        return cmd?.GetValue(null) as string;
    }
}
```

- [ ] **Step 6: 接入 App.xaml.cs**

修改 `src/DshDesktop/App.xaml.cs`：

1. using 区加 `using DshDesktop.DeepLink;`
2. `OnStartup` 中，guard 判定为第一实例之后、创建 `Backend` 之前插入：

```csharp
        DeepLinkRegistrar.Register(Environment.ProcessPath ?? "DshDesktop.exe");
```

3. 在 `_tray.Initialize();` 之后加：

```csharp
        _guard.ArgsForwarded += OnArgsForwarded;
        _guard.StartListening();
```

4. 类内新增方法：

```csharp
    private void OnArgsForwarded(string[] args)
    {
        _mainWindow?.Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
        foreach (var arg in args)
        {
            if (DeepLinkRequest.TryParse(arg, out var request))
                Log.Info($"深链: {request.Action}{(request.SessionId is null ? "" : " " + request.SessionId)}");
        }
    }
```

（`Session` 动作 MVP 只记录日志：会话级 SPA 路由映射在 spec §9 标记为延后项。）

- [ ] **Step 7: 构建 + 手动验证**

```powershell
dotnet build
dotnet run --project src/DshDesktop
```

保持运行，另开 PowerShell：

```powershell
Start-Process 'dsh-desktop://launch'
Start-Process 'dsh-desktop://session/abc'
```

Expected：
1. 两条命令都唤起/聚焦已运行的实例（不出现第二个窗口）；
2. 应用日志出现「深链: Launch」「深链: Session abc」；
3. 注册表 `HKCU\Software\Classes\dsh-desktop\shell\open\command` 默认值指向当前调试 exe 路径。

- [ ] **Step 8: 提交**

```powershell
git add -A
git commit -m "feat: dsh-desktop:// deep link parsing, registration and routing"
```

---

### Task 9: 设置对话框、WebView2 缺失处理、发布与文档

**Files:**
- Create: `src/DshDesktop/SettingsWindow.xaml`、`src/DshDesktop/SettingsWindow.xaml.cs`、`src/DshDesktop/publish.ps1`、`README.md`
- Modify: `src/DshDesktop/MainWindow.xaml.cs`（设置入口、WebView2 缺失处理）、`src/DshDesktop/Tray/TrayService.cs`（设置菜单项）

**Interfaces:**
- Consumes: `AppSettings` / `SettingsStore`（Task 2）、`AppPaths`
- Produces: 可发布的完整应用；spec §5 全部错误路径闭环。

- [ ] **Step 1: 创建设置对话框 XAML**

创建 `src/DshDesktop/SettingsWindow.xaml`：

```xml
<Window x:Class="DshDesktop.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="设置" Width="440" SizeToContent="Height"
        WindowStartupLocation="CenterScreen" ResizeMode="NoResize">
    <StackPanel Margin="16">
        <TextBlock Text="后端端口" />
        <TextBox x:Name="PortBox" Margin="0,4,0,0" />
        <TextBlock Text="dsh 命令（可填绝对路径）" Margin="0,12,0,0" />
        <TextBox x:Name="CommandBox" Margin="0,4,0,0" />
        <CheckBox x:Name="StopBackendBox" Content="退出时停止由本应用启动的后端" Margin="0,12,0,0" />
        <CheckBox x:Name="CloseToTrayBox" Content="关闭窗口时最小化到托盘" Margin="0,8,0,0" />
        <TextBlock x:Name="HintBlock" Foreground="#C62828" TextWrapping="Wrap" Margin="0,12,0,0" />
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
            <Button Content="保存" Width="80" Click="Save_Click" />
            <Button Content="取消" Width="80" Margin="8,0,0,0" Click="Cancel_Click" />
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 2: 实现设置对话框代码**

创建 `src/DshDesktop/SettingsWindow.xaml.cs`：

```csharp
using System.Windows;
using DshDesktop.Settings;

namespace DshDesktop;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        var s = App.Settings;
        PortBox.Text = s.BackendPort.ToString();
        CommandBox.Text = s.DshCommand;
        StopBackendBox.IsChecked = s.StopSpawnedBackendOnExit;
        CloseToTrayBox.IsChecked = s.CloseToTray;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            HintBlock.Text = "端口必须是 1-65535 的整数";
            return;
        }

        var s = App.Settings;
        s.BackendPort = port;
        s.DshCommand = CommandBox.Text.Trim();
        s.StopSpawnedBackendOnExit = StopBackendBox.IsChecked == true;
        s.CloseToTray = CloseToTrayBox.IsChecked == true;
        App.SettingsStore.Save(s);
        App.Log.Info("设置已保存");
        HintBlock.Foreground = System.Windows.Media.Brushes.Green;
        HintBlock.Text = "已保存。端口/命令变更在下次「重试」时生效。";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 3: 替换两处设置入口**

`src/DshDesktop/MainWindow.xaml.cs` 中，把 `Settings_Click` 整个方法体替换为：

```csharp
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow().ShowDialog();
    }
```

`src/DshDesktop/Tray/TrayService.cs` 中，把 settings 菜单项的 Click 处理器替换为：

```csharp
        var settings = new MenuItem { Header = "设置" };
        settings.Click += (_, _) => new SettingsWindow().ShowDialog();
```

- [ ] **Step 4: WebView2 Runtime 缺失处理**

`src/DshDesktop/MainWindow.xaml.cs` 中，把 `InitAsync` 开头两行：

```csharp
        var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebView2UserDataDir);
        await Browser.EnsureCoreWebView2Async(env);
```

替换为：

```csharp
        CoreWebView2Environment env;
        try
        {
            env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebView2UserDataDir);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowOverlay(
                "缺少 WebView2 运行时",
                "需要 Microsoft Edge WebView2 Evergreen Runtime。点击下方按钮前往微软官网下载安装。",
                showRetry: false);
            RetryButton.Content = "下载 WebView2";
            RetryButton.Visibility = Visibility.Visible;
            return;
        }
        RetryButton.Content = "重试";
        await Browser.EnsureCoreWebView2Async(env);
```

并把 `Retry_Click` 开头改为（下载模式下走官网而不是 RetryAsync）：

```csharp
    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (RetryButton.Content?.ToString() == "下载 WebView2")
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    "https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
            }
            catch { /* 忽略 */ }
            return;
        }
        ShowOverlay("正在重连…", "正在重新连接后端…", showRetry: false);
        var ok = await _backend.RetryAsync();
        if (ok && Browser.CoreWebView2 is not null)
            NavigateToBackend();
    }
```

- [ ] **Step 5: 发布脚本**

创建 `src/DshDesktop/publish.ps1`：

```powershell
param(
    [switch]$SelfContained,
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = if ($SelfContained) { "$root/dist/self-contained" } else { "$root/dist/framework-dependent" }

dotnet publish "$root/src/DshDesktop/DshDesktop.csproj" -c Release -r $Runtime `
    -p:Version=$Version `
    --self-contained:$($SelfContained.ToString().ToLowerInvariant()) `
    -p:PublishSingleFile=$SelfContained `
    -o $out

Write-Host "发布完成: $out"
```

- [ ] **Step 6: README**

创建 `README.md`：

```markdown
# dsh-desktop

DeepSeek Harness 的 Windows 桌面薄壳：原生窗口（WPF + WebView2）打开 Harness 的 Web GUI
（默认 `http://127.0.0.1:3080`），自动连接或拉起后端。壳本身零业务逻辑——不代理、不缓存会话状态。

## 构建

要求：.NET 10 SDK（Windows）。

```powershell
dotnet build
dotnet run --project src/DshDesktop
```

## 发布

```powershell
# framework-dependent（~2-5MB，需安装 .NET 10 桌面运行时）
powershell src/DshDesktop/publish.ps1
# self-contained 单文件（~70-100MB，无需运行时）
powershell src/DshDesktop/publish.ps1 -SelfContained
```

## 行为

- 启动时探测 `BackendPort`（默认 3080）：已有 Harness 则 attach；否则自动执行 `dsh web` 并等待就绪（默认 30s 超时）。
- attach 的实例绝不终止；本应用拉起的实例按设置决定退出时是否一并停止（默认否）。
- 离线时窗口显示覆盖层（airspace 限制，离线时折叠 WebView2）；重试按钮重连。
- 单实例：重复启动会聚焦已有窗口；`dsh-desktop://launch` 深链同理（`dsh-desktop://session/<id>` 仅记录日志，路由映射待定）。
- 关闭窗口默认进托盘；托盘菜单可退出、重启后端（仅自己拉起的实例）、在浏览器打开。

## 设置

`%APPDATA%\dsh-desktop\settings.json`：

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `BackendPort` | 3080 | 探测/导航端口 |
| `DshCommand` | `dsh` | 后端启动命令（可绝对路径） |
| `DshArgs` | `["web"]` | 后端启动参数 |
| `StopSpawnedBackendOnExit` | `false` | 退出时停止自己拉起的后端 |
| `CloseToTray` | `true` | 关窗进托盘 |
| `ReadyTimeoutSeconds` | 30 | 拉起后就绪等待超时 |
| `HealthIntervalSeconds` | 5 | 健康检查间隔 |
| `PageMarker` | `__DSH_BOOT__` | 页面内容校验标记 |

日志：`%APPDATA%\dsh-desktop\logs\`（`dsh-desktop.log` 壳日志、`backend.log` 后端输出）。

## 已知限制

- 仅 Windows；应用图标暂为默认（待定）；深链会话跳转未实现；无自动更新。
```

- [ ] **Step 7: 构建 + 全量测试**

```powershell
dotnet build
dotnet test tests/DshDesktop.Tests
```

Expected: 构建成功；`Passed: 31`。

- [ ] **Step 8: 发布验证**

```powershell
powershell src/DshDesktop/publish.ps1
powershell src/DshDesktop/publish.ps1 -SelfContained
```

Expected: `dist/framework-dependent/DshDesktop.exe` 与 `dist/self-contained/DshDesktop.exe` 均生成；分别双击运行，窗口正常打开并加载 Web UI。

- [ ] **Step 9: 最终手动清单（spec §5 全覆盖）**

在你方便时（**退出当前 3080 会话后端之后**）逐项验证：

1. **spawn 路径**：后端不在 → 双击 `dist\framework-dependent\DshDesktop.exe` → 覆盖层「正在启动…」→ 自动拉起 `dsh web` → 加载 Web UI；托盘「重启后端」变为可用；退出时（默认设置）后端仍在 3080 上运行。
2. **崩溃恢复**：`taskkill /IM node.exe /F`（杀掉刚拉起的后端）→ 15s 内覆盖层「后端未连接」→ 点「重试」→ 恢复。
3. **端口被占**：把 settings.json 的 `BackendPort` 改成 80（或另一个被占端口）→ 启动 → 覆盖层「启动失败」。
4. **dsh 找不到**：settings.json 的 `DshCommand` 改成 `dsh-not-exist` → 启动 → 覆盖层「启动失败」，日志含「找不到」。
5. **单实例/深链**：重复启动聚焦；`Start-Process 'dsh-desktop://launch'` 唤起。
6. **托盘全菜单**、关闭进托盘、退出。
7. **F12 / Ctrl+F5 / Ctrl+滚轮 / 外链走系统浏览器 / 下载走默认目录**。

- [ ] **Step 10: 提交**

```powershell
git add -A
git commit -m "feat: settings dialog, webview2 fallback, publish script and readme"
```

---

## Self-Review 记录（计划 vs 规格）

**1. Spec coverage 对照：**

| Spec 条目 | 计划任务 |
| --- | --- |
| §3.2 BackendManager 状态机（attach/spawn/30s 超时/健康 3 连败/仅停自有进程） | Task 4（单元测试全覆盖） |
| §3.2 日志 backend.log / dsh-desktop.log | Task 2 + Task 4（`CreateBackendLogWriter`） |
| §3.3 WebView2 独立 userDataFolder、外链/下载/DevTools/缩放/关窗进托盘 | Task 5 + Task 7 |
| §3.4 托盘（状态行、重启仅自有、浏览器打开、设置、退出、双击恢复） | Task 7 + Task 9 |
| §3.5 深链（注册表、MVP 唤起聚焦、session 只解析） | Task 8 |
| §3.6 settings.json 全部键 | Task 2（默认值）+ Task 9（对话框） |
| §5 错误处理五场景 + WebView2 缺失 | Task 4（spawn/超时/被占/dsh 找不到）+ Task 5（页面加载失败）+ Task 9（Runtime 缺失） |
| §6 测试策略（状态机假实现、HttpListener 探测、损坏 JSON、手动清单） | Task 2/3/4/8 测试 + Task 9 最终清单 |
| §7 项目布局 + 发布（单文件/framework-dependent） | Task 1 脚手架 + Task 9 publish.ps1 |
| §8 实现顺序 | 有意调整：Settings(Task 2) 前置于 BackendManager(Task 4)——依赖顺序要求；其余顺序一致 |

**2. 占位符扫描：** 无 TBD/TODO；Task 5/7 中「Task 9 替换设置入口」为刻意的分阶段实现，已显式标注。

**3. 类型一致性：** `ProbeResult` / `IBackendProbe.ProbeAsync(Uri, string, CancellationToken)` / `BackendManager(BaseUrl, OwnsBackend, RetryAsync, StopOwnedBackend)` / `SingleInstanceGuard(Acquire, SendArgs, StartListening, ArgsForwarded)` / `DeepLinkRequest.TryParse` 在消费任务中的引用与定义任务逐字一致；`App.Settings` / `App.Backend` / `App.Log` / `App.RequestExit` 静态成员跨 Task 5/7/8/9 引用一致。

## Execution Handoff

计划已保存至 `dsh-desktop/docs/superpowers/plans/2026-08-13-dsh-desktop.md`。执行方式二选一：

1. **Subagent-Driven（推荐）**：每个任务派发一个全新 subagent 实现，任务间我做两阶段审查，迭代快。
2. **Inline Execution**：在当前会话按任务批量执行，带检查点供你审查。
