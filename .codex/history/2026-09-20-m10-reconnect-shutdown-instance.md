# M10：自动重连、有界退出与单实例

日期：2026-09-20。延续会话 `20912c30-7dab-47f3-a5e6-98f6f02b83e4` 与 `ba53cf9b-a018-4eeb-bdc2-b171f7bebf8d` 中未交付的 M10 工作。用户「GUI 测试通过，继续 M10。」记录为 M9 GUI E 通过，GUI A / B / C / D 保持此前确认。工作区以 checkpoint `8665720` 为基线继续；保留用户 `.codex/prompt/260919.md` 的改动。

## 目的与实现

让已建立的转发在瞬时断线后自动恢复，退出过程有界并可恢复，且同一数据目录只允许一个实例。范围沿用用户已确认的决定：默认 2 分钟窗口、1 秒初始退避、30 秒上限、±20% 抖动；仅恢复已建立的转发；不缓存临时密码；自动恢复不弹主机信任对话框；关闭窗口仍是退出，未实现托盘。

- `Core/Lifecycle/ReconnectPolicy`、`ReconnectCoordinator`：只对 Connectivity / TimeoutUnknownOutcome 恢复；每台 Server 一个恢复组，票据通过 `ServerWorkLease`（新增 `ServerWorkKind.Reconnect`）登记，删除门禁与退出可取消并排空。预算仅是时间上限，与协调器关闭解耦，避免取消传播顺序混淆超时与退出。
- `Core/Tunnels/TunnelManager.Reconnect`：同一 `ActiveTunnel` 递增 Generation，保留 ID / Requested / Source / StartedAt；先回收旧句柄、会话与登记，再从上次 Actual 端口重新绑定（仅确认的 Local 占用递增），Remote 只申请原端口。成功递增 `ReconnectCount`，不写 Started 活动、不计数。`ReconnectAsync` 把工作票据的取消直接接到 `StopAsync`，修复了迟到连接与退出交错时的复活竞态。
- `Core/Connections`：`AcquireForReconnectAsync` 禁止新的主机批准；信任库错误归为 Storage，不再误判为瞬时故障。`ServerWorkRegistry` 增加 `BeginShutdown` / `ShutdownAsync` / `IsAccepting` / `AvailabilityChanged`，删除屏障释放后通知恢复组重新准入。
- `Core/Lifecycle/ShutdownCoordinator` 重写：命名步骤、已完成步骤计数、超时后保留实际任务、重试只从未完成阶段继续，进度事件供窗口展示。
- `TunnelDeck/Program.cs`、`Services/SingleInstanceService`、`Services/AppLaunchOptions`：同步 STA `Main` 禁用生成入口，按数据目录规范化路径注册 `AppInstance`，在任何配置 / 凭据打开之前完成；第二实例转交激活并以 0 退出，主实例还原最小化窗口。启动异常显示安全 HRESULT 的原生 MessageBox。
- `MainWindow.Lifecycle`、`Controls/ShutdownDialog`：第一次 Closing 取消系统关闭，禁用导航 / 搜索 / 命令，显示进度 InfoBar；10 秒预算执行 Work registry → Deletion → Reconnect → Profiles → Forwarding → Tunnels → Connections → Activity → Configuration。失败弹出 Retry / 「Exit without saving」或「Exit now」（危险色）；Esc 进入「Closing paused」，再次关闭即重试；显式退出在关闭窗口后 `Environment.Exit(0)`。
- `Core/Presentation/TunnelItem`、`Controls/TunnelListItem`：Reconnecting 行显示下一次尝试倒计时、attempt 与「Stop cancels recovery」，按已有 1 秒计时器刷新；Details 将断线前端口标为「Previous listener」，摘要显示自动恢复次数与最近恢复时间。ProfileRunner 对正在恢复的重复请求返回 `AlreadyReconnecting`，不再误报失败或索要密码。
- `Infrastructure/Ssh/SshControlSession`、`SshNetSession`：Remote 传输失败瞬间 Dispose 会被 SSH.NET 2026.0.0 的取消请求打断，二次 Dispose 完成释放；断线路径先完成客户端释放再释放句柄，消除了清理竞态。

## 本次续接完成

- 新增 `tests/TunnelDeck.Ssh.Tests/ResourceCycleTests`：100 次真实 Local + Remote 启停循环，验证会话全部释放、端口可复用，并记录预热后的句柄 / 线程 / 工作集增长到 `artifacts/m10-review/cycles.json`（本次：句柄 +5、线程 0、工作集约 +8 MB）。
- 新增 `scripts/Lifecycle-ForwardingChecks.ps1`，由 `Verify-Lifecycle.ps1` 在实例检查后加载：真实 Profile 转发 → 终止 sshd 及其客户端 → 断线行倒计时 / Details「Previous listener」/ Stop 可用 / 等待期间不写配置 → 恢复后同一行、同一端口、payload 恢复、计数不变 → 退避期间 Stop 只取消该行，另一行照常恢复 → 文件锁下关闭出现 Retry / Exit without saving，Esc 暂停、重试成功保存停止记录 → 显式不保存退出且旧 JSON 不变 → 连续三次关闭合并为一次有界退出。
- 修正 Lifecycle 辅助脚本使用实际的 `ProfileRow_` 行 ID；共享键盘辅助在后台 shell 启动时附加前台线程输入后再切换前台，仍保留所有权检查；Details flyout 改用 UIA click 轻触关闭。
- 新增 `docs/lifecycle.md` 契约。

## 验证与交付

Release x64 锁定还原构建 **0 warnings / 0 errors**。Core **342**、Infrastructure **92**、真实 SSH **78**（含新增的 100 次循环用例），合计 **512 / 512 通过，0 跳过**。相对 M9 新增 38 项 Core、15 项真实 SSH 用例，覆盖退避 / 抖动 / 窗口、共享 Local 与独立 Remote 恢复、Actual 端口优先与确认冲突递增、认证 / host key / 信任库 / 远端拒绝的永久失败、预算耗尽、退避期间 Stop / Delete / Exit、未确认 Remote 申请的所有权、sshd 重启只恢复曾建立的转发、网络黑洞下本地先释放远端延后释放、注册表关闭与协调器重试。

原生检查 `Verify-Lifecycle.ps1` 的实例与转发场景结果记录于 `artifacts/m10-review/native-lifecycle/result.json`，M10 证据汇总于 `docs/evidence/2026-09-20-m10.json`。M9 的 `artifacts/m9-review` 快照未被覆盖。

人工评测只需查看断线等待与关闭过程的信息是否清楚（GUI F），网络故障注入由 Agent 完成。托盘、导入导出、Terminal 与完整统计仍留给后续里程碑；Agent 未切换系统 High Contrast。
