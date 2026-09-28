# M6 转发引擎契约

更新：2026-09-20。`Core/Tunnels/TunnelManager` 可以脱离 WinUI 使用，已接入 `AppServices.Tunnels` 和正常退出流程。M6 完成引擎，M7 已通过 `TunnelWorkspace` 接入真实命令、稳定活动行和历史记录，详见 [quick-forward](quick-forward.md)。

## 入口与结果

```csharp
TunnelStartResult result = await tunnels.StartAsync(launchSnapshot, credentials, cancellationToken);
ImmutableArray<TunnelBatchItem> batch = await tunnels.StartAllAsync(ruleSnapshots, credentialsByServer, cancellationToken);
TunnelStopResult stopped = await tunnels.StopAsync(result.Tunnel.Id);
await tunnels.StopServerAsync(serverId);
await tunnels.StopAllAsync();
bool removed = await tunnels.RemoveAsync(result.Tunnel.Id);
```

`RuleLaunchSnapshot` 复制 Server、Requested 规则和来源名称。运行条目不写回配置；Profile 改名、编辑或删除不改变已有实例。服务器连接身份在登记 Starting 前通过 `ServerUseLease` 预留，预留后再次对比快照身份；过期快照失败，不静默连接到新地址。

- `TunnelStartResult.Succeeded` 仅表示返回快照为 Running；`Reused` 表示复用已有 Starting / Running 请求。等待的实例若被停止，Start 可以返回 Stopped / Cancelled 快照，调用者不能把“Task 正常返回”当成成功。
- 单条输入校验失败会抛出异常；网络与监听失败保留为带安全 `LastError` 的运行条目。调用者取消自己的 Start 等待会抛出 `OperationCanceledException`。
- `StartAllAsync` 在开始时复制整批输入，返回顺序与输入顺序一致。每项独立携带 Result / Failure；已有成功项保持运行。Succeeded + Reused 对应 already running，其他成功项对应 started；取消、失败与被 Stop 中断的条目按返回状态呈现。
- 批量取消停止尚未完成且没有其他等待者的请求，已成功交付的规则继续运行。需要停止整批运行实例时调用 Stop / StopAll，不把批量取消当成回滚。
- `StopAsync` 幂等。Stop 调用者取消只取消等待，已经接收的清理继续执行。清理失败返回 `Succeeded=false`，条目保持 Stopping；再次 Stop 可重试。
- `RemoveAsync` 仅在清理成功后移除条目。StopAll / StopServer 在排空期间阻止相应范围的新请求，返回每条的停止结果。

## 身份、重复请求与调度

去重键是 `ForwardRequestKey`：ServerId + Mode + 两端 Requested host / port。RuleId、ProfileId、自动搜索开关和 Actual 端口不参与去重。并发 Enter / 双击只登记一次，来源保留首次创建者；复用不建立隐含的 Profile 引用计数，任一规则级 Stop 会停止这个实例。

正在停止的相同请求必须等清理完成才创建新实例；清理失败仍占用请求键和服务器身份。停止后重启获得新的 TunnelId，旧的失败 / 停止条目由后续 UI 移除或展示。

同 ServerId 的监听 Start / Stop / Dispose 通过一个门禁串行；认证和用户指纹确认在该门禁之外。全局默认最多 4 个启动任务、4 个清理任务。不同 ServerId 的本机监听仍竞争同一操作系统端口；服务器身份不同不意味着可以共享本地 socket。

Local 共享 Local-only session；每条 Remote 独占 session，见 [ADR 0001](decisions/0001-ssh-control-isolation.md)。首连的相同 host / SSH port / algorithm / fingerprint 共用一次确认和信任保存，认证和 Remote 传输仍独立。取消一个等待者不关闭其他等待者的对话框；最后一个取消必须等待对话框真正结束，迟到确认不能授信。不同指纹绝不合并，保存使用禁止覆盖其他 key 的 `TrustNewAsync`。

## 真实端口分配

`PortSearchPolicy` 产生候选，`TunnelManager` 对每个候选执行真正的 SSH.NET Start。生产路径没有“先探测空闲再绑定”。

| 情况 | 行为 |
| --- | --- |
| Local + 开启自动搜索 + 确认 `SocketError.AddressAlreadyInUse` | 按 N、N+1、N+2 顺序申请，只改变 LocalPort |
| Local 自动搜索关闭 | 仅申请一次 |
| Local AccessDenied / 保留端口、输入、认证、信任、连通性、取消、超时 | 返回错误，不递增 |
| Remote，包括错误携带自动搜索标志的旧输入 | 仅申请指定远端端口一次，不递增 |
| Remote 明确拒绝 | `RemoteListenFailure`；端口占用、AllowTcpForwarding 和 PermitListen 的具体原因未知 |
| 监听确认超时 | `TimeoutUnknownOutcome`；失效所属物理传输并排空工作，不能继续复用它 |

默认上限 20 次（包含 N），总搜索时间 10 秒，单次最多 2 秒，末次最多使用剩余时间；到 65535 停止，不绕回。搜索预算从获取连接并进入该服务器控制门禁后开始，不包含用户输入指纹确认的时间。认证使用独立连接预算。

Requested 及目标端始终不变。成功时记录 Actual、BindAttempts、PortSearchStopReason；仅实际调整的 Local 规则记录 `AdjustmentReason=LocalListenerInUse`。失败候选的 ForwardedPort、订阅和注册由适配器释放。Remote 拒绝导致的传输关闭不能把已确认的拒绝覆盖成通用断线错误。

## 生命周期与清理

每个运行实例持有预留身份、连接 lease、监听句柄及未完成的 Start / Stop / Dispose 任务。建立成功后进入 Running；显式停止、失败、取消或断线先进入 Stopping，完成清理后才进入 Stopped / Failed / Cancelled。

同步 SSH.NET 操作在受控后台执行。`WaitAsync` 取消不等于库停止：`SshControlSession` 在同一锁内登记 worker 和禁止新控制操作，取消 / 超时后关闭 client，持续观察 worker 完成。若适配器忽略取消后才返回句柄，管理器仍取得该句柄并释放它，不能丢下后台工作后宣称清理完成。

Start、单个或最后一个重复调用者取消、Stop、Remove 和 Shutdown 的交错都保持同一所有权。Stop / Dispose 默认确认预算为 5 秒；未确认时通过 `SshSessionLease.InvalidateAsync` 失效整个物理传输。Local 共享传输失效会更新相关 Local 规则，Remote 只影响自己的传输。保留清理失败的任务与句柄，重试 lease 释放不会重复扣减借用者。

App 关闭顺序为 **Work registry → Deletion → Reconnect → Profiles → Forwarding → Tunnels → Connections → Activity → Configuration → 可选窗口 probe**。M9 先排空删除事务，再排空 ProfileRunner / Quick 命令、取消临时凭据输入，清理网络并保存离散历史；最后在窗口仍存活时取消并排空原生对话框，再关闭窗口。Shutdown 永久停止接收新转发；所有已接收的监听和连接工作排空之后才完成。外层窗口超时只结束等待，后台清理仍被追踪。删除门禁和失败一致性见 [deletion](deletion.md)。断线后已建立的转发进入 M10 自动重连，未建立的启动仍标记失败并清理；见 [lifecycle](lifecycle.md)。

正常可达的测试服务器上，清理完成后已验证 Local 可重新绑定、Remote 不再监听。网络黑洞、崩溃或断电时，远端释放依赖 sshd 检测到连接丢失；不能把本地 Dispose 完成扩展为所有网络故障下的即时远端释放承诺。

## 活动、错误与 UI 订阅

`Current` 返回不可变 `TunnelManagerSnapshot`；StateChanged 可以从后台线程触发。M7 的 TunnelWorkspace 通过 IViewDispatcher / DispatcherQueue 更新界面，丢弃较旧 Revision，保留相同 ID 的行对象并在释放时退订。RunningCount 只统计 Running。

`EndedAt` 在清理完成时记录，Failed / Cancelled / Stopped 均保留实际结束时刻。TunnelActivityRecorder 直接监听引擎，按运行 ID / 阶段保存离散事件；确认成功的新实例更新累计次数与 Server.LastUsedAt。UI duration 和请求计数不写盘，历史提交失败可重试；正常 Stop 在清理与历史提交成功后移除行，失败条由用户 Retry / Remove。

`ForwardActivity` 是每个句柄的单调版本快照。并发事件可能乱序送达，引擎只接受新版本，避免计数倒退。AcceptedConnections 统计库观察到的转发连接请求，不能当成目标连接成功数。StartedAt / LastConnectionAt 在本次监听确认成功时写入；LastRequestAt / LastRequestFailureAt 记录观察到的请求时间。

请求错误使用独立的 RequestFailureCount / LastRequestError，不覆盖监听的 LastError，也不会仅因目标服务不可达而销毁 listener。重复请求错误默认每 2 秒至多通知一次，计数与最新错误仍持续更新；成功请求不会重新通报旧错误。纯请求计数可在详情打开期间读取 Current，无需为每个 TCP 请求刷新整个 UI。

固定版本 **SSH.NET 2026.0.0 的观测边界**：

- Local 的 direct-tcpip OpenFailed 没有完整转发到 `ForwardedPortLocal.Exception`。目标不可达测试看到客户端 EOF，监听保持 Running；目标启动后同一监听可正常传输。未观察到的错误不编造成目标健康或占用诊断。
- Remote 目标连接失败可通过 Exception 观察并记录。受控 OpenSSH 测试中，客户端提前发送数据时可能等到读取超时，而不是立即收到 EOF；测试明确记录客户端结果，并独立断言请求错误及后续恢复传输。
- 不解析 SSH 日志字符串，不反射库内部状态，不远程执行 shell 命令推断监听拒绝原因；不把请求数当作字节流量。

M7 已修复 forwarded-tcpip 通道不答复 OpenSSH channel keepalive 的兼容问题：在创建 SshClient 前移除公开 ChannelRequests 字典中对应项，使用库既有的未知请求失败答复。严格存活探测下，通道空闲七秒后仍能传输和正常停止，详见 [ADR 0002](decisions/0002-forwarded-channel-keepalive.md)。TCP half-close 传播与长期通道回收不在该修复的已验证范围内。

## 验证

`./scripts/Test.ps1 -Ssh` 重建受控 OpenSSH 8.9p1 夹具并运行三套测试；镜像未改动时可使用 `-NoFixtureBuild`。M6 基线 372、M7 基线 404；M8 当前 Core 283、Infrastructure 87、真实 SSH 60，共 **430 项通过、0 跳过**，包含以下全部引擎回归。

| 计划用例 | 证据 |
| --- | --- |
| TUN-01 | EngineForwardingTests：Local / Remote 的 1 MiB echo、8 个并发连接、HTTP 内容一致 |
| TUN-02 / 03 | EngineAllocationTests + TunnelAllocationTests：真实连续占用、最小可用端口、关闭搜索、20 次耗尽、65535、虚拟时间边界、不可递增错误、三类 Remote 拒绝 |
| TUN-04 / 05 | 同请求并发去重、跨服务器本地端口竞争、共享 Local 单条停止、原端口重用及其他规则继续传输 |
| TUN-06 | TunnelLifecycleTests、SshControlSessionTests、EngineLifecycleTests：迟到成功、持有真实 Remote 确认、取消 / Remove / Shutdown、清理失败重试与状态不复活 |
| TUN-07 | 两种模式目标先不可达、启动目标后复用 listener 传输；保留上述 Local 诊断限制 |
| TUN-08 | ShutdownCoordinator 真实 Local / Remote 释放，以及不确定 Remote Stop 对其他规则的隔离 |

新增测试还覆盖批量顺序、部分成功、并发上限、停止期间准入、活动乱序、重复诊断聚合、指纹确认合并与独立取消。测试夹具的本机端口预留避开测试主机配置的动态 TCP 范围；此预留仅用于测试，不进入生产 allocator。

M7 增加通道空闲存活与夹具 HTTP 可靠性，M8 增加 Profile 跨服务器 Local / Remote、部分失败重试、真实远端确认待回复时取消三项用例；M9 增加真实 Profile / Server 删除、监听释放及删除迟到确认时另一服务器继续传输。ProfileRunner 复用本引擎，契约见 [profiles](profiles.md) 与 [deletion](deletion.md)。最新构建与证据索引见 [milestones](milestones.md)，旧里程碑证据保留。GUI A / B / C / D 已由用户确认，M9 GUI E 待评测。
