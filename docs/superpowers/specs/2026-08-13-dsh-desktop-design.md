# dsh-desktop 设计文档

- 日期：2026-08-13
- 状态：待用户评审
- 目标形态：Windows 桌面薄壳，为 DeepSeek Harness 的 Web GUI 提供一个 Codex 式桌面入口。壳本身零业务逻辑，只做窗口、进程托管与 OS 集成。

## 1. 背景与目标

### 1.1 为什么是薄壳

后端（DeepSeek Harness，`dsh web` profile）已经是一个本地 HTTP 服务，默认监听 `http://127.0.0.1:3080`，同时托管前端 SPA 与完整 API。其传输协议（`@deepseek-ai/dsh-client-connection`）已经确定：

- 客户端 → 后端：HTTP 上行（`/api`，Typert RPC）。
- 后端 → 客户端：两条 WebSocket 下行流 `/api/events.mux`（会话事件复用帧）与 `/api/events.host`（宿主级帧），token 流、工具调用、job 状态均为实时推送；客户端侧的 `ConnectionController` 自带断线重连。
- 会话状态持久化在 `DSH_HOME`（jsonl + sqlite），后端是唯一事实源。

因此「实时同步 vs 异步」不需要桌面端再做选择：**命令异步（HTTP）、状态实时（WS 推送）、桌面端零状态**。桌面壳不代理、不缓存、不复制任何会话状态，只处理页面整体不可用的情形（后端进程退出）。

### 1.2 目标

1. 原生 Windows 窗口（WPF + WebView2）打开现有 Web UI，观感与使用方式接近独立桌面应用。
2. 启动时优先连接已在运行的后端；未监听时自动拉起 `dsh web` 并等待就绪后导航。
3. 单实例 + 系统托盘 + `dsh-desktop://` 深链唤起。
4. 壳与后端进程隔离：壳崩溃不影响后端；后端重启时壳可重新导航恢复。
5. 不改动 Harness 后端任何代码；`dsh-desktop` 为独立项目。

### 1.3 非目标（YAGNI）

- 不重新实现任何会话/聊天/工具 UI（沿用现有 SPA）。
- 不做多窗口、多 profile 切换、会话管理增强。
- 不引入 Rust/Tauri、不跨平台（仅 Windows）。
- 不做自动更新、安装器（MVP 为 zip 或单文件发布，后续再议）。
- 深链的会话级跳转（映射到 SPA 内部路由）不在 MVP 内，见 §9。

### 1.4 已确认决策（用户选择）

| 决策点 | 结论 |
| --- | --- |
| 桌面端形态 | A. WebView 壳包住现有 Web UI |
| 平台与技术栈 | 仅 Windows：WPF + WebView2（.NET 10，当前 LTS） |
| 后端生命周期 | 两者都要：优先 attach 已有实例，连接不上自动拉起 |
| OS 级功能 | 系统托盘、深链协议（dsh-desktop://）、单实例锁 |
| 开机自启 | 不做 |

### 1.5 环境实测（2026-08-13）

- .NET SDK 10.0.301，WindowsDesktop 运行时 10.0.9（仅 10.x，故目标框架 net10.0-windows）。
- WebView2 Evergreen Runtime 151.0.4129.78 已装。
- `dsh` 经 npm 安装在 PATH（`dsh.cmd` / `dsh.ps1` shim 均在）。
- `http://127.0.0.1:3080/` 返回 200，`<title>DeepSeek Harness</title>`，HTML 含 `__DSH_BOOT__`（健康检查内容校验用此标记）。

## 2. 架构总览

```
┌────────────────────────── dsh-desktop.exe ──────────────────────────┐
│  MainWindow (WPF)                                                   │
│   └─ WebView2 ───────navigate──────► http://127.0.0.1:3080         │
│                                        ▲ 现有 SPA + /api + WS       │
│  BackendManager                        │ （页面自己的 HTTP↑/WS↓，    │
│   ├─ 探测端口 3080 ──未监听──► spawn `dsh web`                       │  壳不代理、不拦截）│
│   └─ 健康检查 / 等待就绪                                             │
│  TrayIcon（托盘）· SingleInstance（互斥锁+管道转发）· DeepLink（注册表）│
└─────────────────────────────────────────────────────────────────────┘
```

关键原则：

- **壳是纯客户端**：与后端之间只有「就绪探测」与「自己拉起的进程的生命周期控制」两类交互，除此之外零通信。
- **同源直连**：WebView2 直接导航 `http://127.0.0.1:3080`，天然通过后端的 Host/Origin 信任栅栏（`dsh-client-connection` 的 `isTrustedApiRequest`），无需配置 `trustedHosts`。
- **壳无状态**：窗口位置、设置等壳自身状态存 `%APPDATA%\dsh-desktop\settings.json`；会话数据一概不碰（留在 `DSH_HOME`）。

## 3. 组件设计

### 3.1 `App` / `SingleInstance`（单实例）

- 命名 Mutex（`Local\dsh-desktop-single-instance`）保证单实例。
- 第二实例启动时：若带参数（深链），经命名管道把参数转发给首实例 → 首实例聚焦并处理 → 第二实例退出。
- 与 DeepLink 共享同一套「参数解析 → 动作路由」。

### 3.2 `BackendManager`（后端生命周期）

状态机：

```
Detecting ──端口已监听──► Attached ──导航就绪──► Online
    │
    └─端口未监听──► Spawning ──spawn `dsh web`──► WaitingReady（轮询 ≤30s）
                        │                              │ 就绪
                        │ 失败                          ▼
                        ▼                             Online
                    Failed ──► 覆盖层：重试 / 打开设置
Online ──健康检查失败（连续 3 次）──► Offline ──► 覆盖层：重试（重新导航/重新拉起）
```

规则：

- `Attached` 时**绝不**终止后端进程（非我启动，不归我管）；`Spawning` 拉起的实例才记录 PID，退出时按设置决定是否一并停止（默认否）。
- spawn 方式：优先 `PATH` 上的 `dsh`（Windows 下经 `dsh.cmd` shim，`cmd /c dsh web`）；设置里可覆盖绝对路径与端口参数。
- 就绪探测：TCP connect + HTTP GET `/` 返回 200。端口默认 3080，设置可覆盖（实现时以 `dsh web --help` 确认端口参数名与写法）。
- 健康检查：Online 后每 5s 一次轻量 HTTP 探测；探测同时做**内容校验**（响应 HTML 含 Harness 标记，如 `<title>`/`__DSH_BOOT__` 注入特征，实现时以实际页面为准），用于区分「后端宕了」与「端口被非 Harness 服务占用」；连续 3 次失败进入 Offline。
- 日志：`%LOCALAPPDATA%\dsh-desktop\logs\dsh-desktop.log`（壳自身日志；后端 stdout/stderr 记入 `backend.log` 便于排查）。

### 3.3 `MainWindow` + WebView2

- WebView2 使用 **Evergreen Runtime**（Win10/11 普遍自带）；独立 userDataFolder：`%LOCALAPPDATA%\dsh-desktop\WebView2`（会话 cookie 等与浏览器隔离）。
- 覆盖层（WPF 元素盖在 WebView2 上）三态：`启动中…` / `未连接（后端离线），[重试] [打开设置]` / 隐藏。
- `NewWindowRequested`（含 `target="_blank"`）一律交给系统默认浏览器打开，不在壳内开新窗。
- 下载：`DownloadStarting` 交给默认下载流程（默认目录，提示路径）。
- 快捷键：`F12` 开关 DevTools；`Ctrl+滚轮` 缩放；`Ctrl+F5` 硬刷新。
- 关闭窗口 = 最小化到托盘（设置可改为直接退出）；托盘「退出」才真正退出。

### 3.4 `TrayIcon`（托盘）

- 图标三态：后端 Online（正常色）/ 后端 Offline（警示色）/ 启动中（转圈或灰色）。
- 菜单：显示主窗口 / 在浏览器中打开（`127.0.0.1:3080`）/ 后端状态行（只读）/ 重启后端（仅壳拉起的实例可用）/ 设置 / 退出。
- 实现：`Hardcodet.NotifyIcon.Wpf`（纯 WPF 托盘库）或等价物。

### 3.5 `DeepLink`（深链）

- 注册 `HKCU\Software\Classes\dsh-desktop` → `shell\open\command` → `"<exe路径>" "%1"`（安装/首次运行时写入，退出不清除）。
- MVP 语义：`dsh-desktop://` 仅用于**唤起 + 聚焦窗口**；`dsh-desktop://session/<id>` 等会话级跳转暂只解析保留参数，路由映射待确认现有 SPA 路由格式后实现（§9）。

### 3.6 `Settings`

- 路径：`%APPDATA%\dsh-desktop\settings.json`；简单 JSON，读写用 `System.Text.Json`，缺失项回落默认值。
- 键：
  - `backendPort`（默认 3080）
  - `dshCommand`（默认 `dsh`；可设绝对路径，如 `C:\…\dsh.cmd`）
  - `dshArgs`（默认 `["web"]`）
  - `stopSpawnedBackendOnExit`（默认 `false`）
  - `closeToTray`（默认 `true`）
  - `windowState`（位置/尺寸记忆）
- 极简设置对话框（覆盖层「打开设置」入口直达）。

## 4. 数据流

1. **页面 ↔ 后端（不变）**：命令 HTTP 上行；事件 WS 下行（`/api/events.mux`、`/api/events.host`）。壳不参与、不改写。
2. **壳 ↔ 后端（仅两类）**：
   - 启动/就绪探测：TCP connect + `GET /`（启动时与 Online 期间的健康检查）。
   - 生命周期控制：仅对壳拉起的 PID 做停止/重启。
3. **深链/第二实例 → 首实例**：命名管道转发启动参数（纯文本，无状态）。

## 5. 错误处理

| 场景 | 处理 |
| --- | --- |
| 端口未监听且 `dsh` 找不到（spawn 失败） | 覆盖层：提示在设置中配置 `dshCommand`；[打开设置] [重试] |
| spawn 后 30s 未就绪 | 判定 Failed，覆盖层显示后端日志尾部；[重试] [打开设置] |
| 端口被非 Harness 服务占用 | 导航后若页面/API 异常（同源栅栏会 403），健康检查应带内容校验（`GET /` 返回含 Harness 标记的 HTML）判定 Offline；提示用户端口冲突 |
| 后端运行中崩溃（Attached 或 Spawned） | 健康检查连续失败 → Offline 覆盖层；[重试] 对 Spawned 实例重新拉起、对 Attached 重新探测/导航 |
| WebView2 Runtime 缺失 | 启动时检测 `CoreWebView2Environment.GetAvailableBrowserVersionString()` 抛异常 → 显示 Evergreen Runtime 下载链接页（微软官方链接），不崩 |
| 页面内 WS 断线 | 不处理——页面自带重连（`ConnectionController`），壳只在整页不可达时介入 |
| 单实例第二进程启动 | 参数转发后退出，退出码 0 |

## 6. 测试策略

- **单元测试（xUnit）**：
  - `BackendManager` 状态机：用真实临时 TCP 监听器测 Attach 路径；用注入的 `IProcessRunner` 假实现测 Spawn/Failed/Offline 转换与 30s 超时。
  - `Settings`：读写、默认值回落、损坏 JSON 恢复默认。
  - `DeepLink` 参数解析：合法/非法 URI。
- **集成/手动清单（MVP 以手动为主）**：
  - 启动时后端已在跑 → 直接 Attach 导航，不 spawn。
  - 后端未跑 → 自动拉起、就绪后导航；退出壳时按设置决定是否停后端。
  - 运行中 `taskkill` 后端 → Offline 覆盖层出现 → 重试恢复。
  - 单实例：二次启动聚焦已有窗口；深链唤起同理。
  - 托盘各菜单项、关窗进托盘、退出真正退出。
  - DevTools、缩放、外链走系统浏览器、下载走默认目录。
- **不做**：WebView2 内容级自动化测试（成本高、收益低，SPA 本身由 Harness 侧负责）。

## 7. 项目布局

```
dsh-desktop/
├─ src/DshDesktop/                 # WPF 应用（net10.0-windows）
│  ├─ App.xaml / App.xaml.cs       # 启动、单实例、深链入口
│  ├─ MainWindow.xaml(.cs)         # WebView2 宿主 + 覆盖层
│  ├─ Backend/                     # BackendManager、PortProbe、HealthProbe、IProcessRunner
│  ├─ Tray/                        # TrayIcon 与菜单
│  ├─ DeepLink/                    # 协议注册、参数解析
│  ├─ SingleInstance/              # Mutex + 命名管道转发
│  ├─ Settings/                    # settings.json 读写与默认值
│  └─ assets/                      # 应用/托盘图标
├─ tests/DshDesktop.Tests/         # xUnit
├─ DshDesktop.sln
├─ README.md                       # 构建、发布（zip/单文件）、使用说明
└─ docs/superpowers/specs/         # 本文档
```

- 发布：`dotnet publish -c Release -p:PublishSingleFile=true --self-contained` 产出单文件 exe（体积 ~70-100MB，含 .NET 运行时）；framework-dependent 版本（~2-5MB，需装 .NET 10 桌面运行时）作为轻量选项，README 说明取舍。

## 8. 实现顺序（供后续 writing-plans 细化）

1. 最小壳：MainWindow + WebView2 直连 3080（先手动跑 `dsh web`）。
2. BackendManager：探测 / spawn / 就绪等待 / 健康检查 + 覆盖层。
3. Settings + 日志。
4. 单实例 + 托盘。
5. 深链（唤起聚焦 + 参数解析骨架）。
6. 错误处理补齐、手动测试清单执行、发布脚本。

## 9. 待确认 / 延后项

| 项 | 状态 |
| --- | --- |
| 深链会话级跳转的 SPA 路由格式 | 延后；实现第 5 步前查阅现有前端路由（`dsh-client-ui-*`）再定 |
| `dsh web` 的端口参数名 | 实现第 2 步前用 `dsh web --help` 确认 |
| 应用名称与图标 | 暂用 `dsh-desktop` 占位，待用户定 |
| 安装器 / 自动更新 | 延后（MVP 为 zip / 单文件） |
| 非 loopback（连局域网 Harness） | 延后；届时走 `trustedHosts` 配置，壳侧只需放开 URL 白名单 |
