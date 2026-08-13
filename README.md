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

## 图标

应用与托盘图标使用 DeepSeek Harness 的鲸鱼图形（`dsh-web-frontend/dist/favicon.svg`，MIT），
以品牌蓝 `#5686FE`（`--dsw-static-deepseek-450`）为底重新渲染：
`src/DshDesktop/assets/app.ico`（256→16 多尺寸）与 `tray-{online,offline,starting}.png`（托盘三态，
底色分别为品牌蓝 / 红 / 灰）。原始 SVG 存于 `src/DshDesktop/assets/whale.svg`。

## 已知限制

- 仅 Windows；深链会话跳转未实现；无自动更新。
