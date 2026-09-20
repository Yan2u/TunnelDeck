# M7 Quick Forward 与 Active Tunnels

更新：2026-09-20。M7 将 Dashboard 接入真实转发引擎，GUI C 已由用户「GUI 通过，继续 M8.」确认；实现与验证结果见 [milestones](milestones.md) 和 [M7 证据](evidence/2026-09-20-m7.json)。M8 已接入 [Profile 规则编辑与批量启动](profiles.md)，自动重连留给 M10，Open Terminal 留给 M11。

## 输入与提交

`QuickForwardModel` 持有窗口生命周期的草稿。默认单端口映射两端同一端口，地址均为 `127.0.0.1`；展开 Advanced endpoints 后分别编辑 Local / Remote 的地址与端口。首次展开复制当前单端口值，后续收起、展开和页面导航保留各自草稿。Local 的地址标签表示本机监听和远端目标；Remote 表示本机目标和远端监听，方向说明随模式变化。

当前 Server 选择跨页面保留；Quick 固定选择单独保存到 `PinnedQuickServerId`，与资源列表的 Pin 无关。初始化优先使用有效固定选择，其次 Settings 默认；配置更新不覆盖仍然有效的当前选择。ComboBox 更换 ItemsSource 期间的临时空选择不清除草稿，真正的资源失效由 Core 的 Reconcile 处理。全局自动找端口设置只在首次数据加载后初始化草稿，不覆盖已编辑值。

`Controls/PortInput` 使用原生 NumberBox，关闭数值规范化校验并捕获内部 TextBox 的原始文本。Enter 在 NumberBox 的 KeyUp 规范化之前提交；Forward 点击也使用同一份文本。`ForwardRuleDraft.Build` 拒绝小数、NaN、指数、分组、空值和 1–65535 范围外的端口，错误显示在对应字段旁，不回退到上次有效的 NumberBox.Value。实际输入的 AutomationId 为 `QuickPortText`、`QuickLocalPortText`、`QuickRemotePortText`。

页面加载且配置就绪后只执行一次初始 focus：已有 Server 时聚焦当前端口输入。提交后保留输入并选中端口文本，方便连续启动；后台通知不重新执行初始 focus。无 Server 时 Forward 禁用并提供 Add server；只读存储、关闭中、预览和正在提交时也禁止新启动。

Local 自动搜索只处理引擎确认的 `AddressAlreadyInUse`。Remote 隐藏选项并说明需手动调整，生成的 Remote 规则始终关闭自动搜索。界面不探测空闲端口、不改 Requested，也不改写已保存规则，完整分配策略见 [tunnels](tunnels.md)。

## 命令、凭据与幂等

`AppServices.Forwarding` 是可脱离 WinUI 测试的 `TunnelWorkspace`。它向 `AppServices.Tunnels` 提交不可变 `RuleLaunchSnapshot`，提供 SubmitQuick / Stop / StopAll / Retry / Remove / RetryActivity；Page 只负责绑定、focus、导航和原生控件事件。

密码未保存且确实需要新连接时，先用排队的原生 ContentDialog 收集本次密码，取消不会创建虚假的失败记录。相同请求或可用的共享 Local 传输可直接复用。保存的凭据缺失、认证失败或私钥需要口令时，保留引擎的安全失败结果并提供临时凭据输入和重试；取消后仍可从失败行重试。无口令私钥允许空输入，身份文件路径在 Server 详情页编辑。

此对话框不写 Credential Manager，也不将秘密缓存到 Quick 草稿、运行快照或日志。对话框关闭时清空 PasswordBox；要保存凭据需在 Server 详情页明确选择 Remember 并 Save。未知 host key 仍经过既有的指纹确认流程，临时输入不绕过身份校验。

提交期间的 busy 门禁和引擎 Requested 去重共同防止重复实例；完全重复请求保持已有行和来源，累计启动次数不增加。Retry 使用原 Requested 和来源快照、当前有效 Server 配置；清理旧失败实例后再启动新的实例，因此成功重试会产生新的运行 ID。每条 Remote 保持独立物理传输，输入的临时密码不跨新 Remote 连接缓存。

## 活动行、详情与滚动

`TunnelItem` 包装不可变 `ActiveTunnel`，同一运行 ID 保持同一个可观察行对象。后台 `StateChanged` 经 `IViewDispatcher` / WinUI DispatcherQueue 发布，旧运行 revision 和旧配置 revision 不覆盖新状态。Shell 使用单独的运行通知更新 N active，普通隧道事件不重建 Sidebar；确认启动后的 LastUsedAt 提交可以更新 working set。

- Starting / Running / Stopping / Reconnecting 和失败终态均可见；N active 只统计 Running。状态有文字，pending 状态带进度指示。
- 正常 Stop 等待清理与活动提交成功后从列表移除，结果保留在 Recent。停止失败保持行和所有权，可再次 Stop；Failed / Cancelled 保留 Retry 和 More → Remove。
- Local 的实际监听端口变化时，在该端口后紧邻放置可聚焦 Button + Attention IconInfoBadge。Flyout 解释 Requested → Actual 及占用原因；Remote 没有调整标识。
- Details Flyout 显示 Requested / Actual、开始时间、duration、来源快照及实际观察到的请求错误。Flyout 放在按钮左侧，避开顶部 caption；键盘可打开，Esc 关闭。
- duration 仅在 Details 打开时每秒刷新，关闭或控件卸载即停 timer。`EndedAt` 在实际清理完成后记录，终态 duration 固定；tick 和请求计数不写盘。
- 来源名称始终保留快照。当前资源仍存在时提供 Server / Profile 导航，资源已删除时移除或禁用对应入口。

Active Tunnels 使用有高度上限的 ListView：页面高度的 45%，限制在 180–420 epx，并受可用空间约束。长列表在自己的 viewport 滚动，Quick Forward 不随列表内容移动。页面 ScrollViewer 负责其余正文；Recent 最多显示五条。24 行预览覆盖列表滚动、键盘焦点与 1200 / 900 / 640 / 500 epx 的 Light / Dark 布局。

Running 只表示监听建立。请求异常与监听异常分开显示；SSH.NET 对 Local direct-tcpip OpenFailed 的公共通知不完整，不能据此承诺每次目标拒绝都有应用诊断，也不能把请求数当作目标成功数或字节流量。

## Recent、累计次数与关闭

`TunnelActivityRecorder` 直接订阅引擎快照，将 Started / Stopped / Failed / Cancelled 离散事件通过 `ConfigurationService` 的 FIFO 提交写入已有 schema 1。按运行 ID 的启动 / 结束阶段去重；即使终态先于 Running 通知到达，也可由 StartedAt 补出成功启动事件。保留最近 200 个事件，裁剪不影响累计次数。

每个新实例首次确认成功启动时，累计 `TotalForwards` 和对应 `ServerForwards` 加一，并前推 Server.LastUsedAt；重复提交、连接测试、请求流量和 duration 不增加次数。重试后新实例成功可以计数。M8 已增加按 Profile 启动操作计数的 ProfileUses / LastUsedAt，至少有一个新成功实例才计一次，详见 profiles；重连口径和完整设置仍按后续里程碑完成。

写盘失败不会停止已建立的隧道：事件留在内存队列，Dashboard 显示可重试提示。正常停止的行暂时保留为 Stopped，Retry saving activity 成功后再移除；重试不重复计数。队列尚未落盘时若进程被强制终止，不能保证这些事件保留。

M8 的关闭顺序为 **Profiles → Forwarding → Tunnels → Connections → Activity → Configuration → 可选窗口 probe**。先排空 Profile 批量任务，再禁止 Quick 新命令、取消凭据对话框并排空已接收命令，释放真实监听和 SSH 连接，最后刷新活动与配置。组件释放时退订事件，已排队 UI 回调不会更新已释放对象。窗口关闭的超时与重试继续沿用 ShutdownCoordinator。

## 自动化与 GUI C

```powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Test.ps1 -Ssh
./scripts/Verify-Forwards.ps1 -NoFixtureBuild
./scripts/Verify-Dashboard.ps1
```

| 计划用例 | 自动化证据 |
| --- | --- |
| VM-01 | QuickForwardTests / TunnelWorkspaceTests：原始输入、默认值、命令禁用、并发重复、临时凭据、重试 / 移除；原生 Enter / 固定选择重启 / 初始 focus |
| VM-02 | TunnelItemTests + 原生占用 N / N+1：只调整 Local 到 N+2，Requested 和远端目标不变，实际传输 payload |
| VM-03 | 终态 duration、来源删除后保留名称、只统计 Running；原生 Details 打开 tick 不增加配置 revision |
| VM-04 | 后台事件排队、旧 revision 防护、稳定行对象、关闭取消对话框、Dispose 后回调不发布；预览直接调用命令也不能联网 |
| 真实 UI 闭环 | 新建 / 固定 Server、认证 / 指纹、两条 Local 与两条 Remote、远端拒绝后手动重试、独立 Stop、带运行隧道关闭、四次启动 / 四次正常停止落盘 |
| 布局与键盘 | 显式 24 行预览、Quick 位置不随活动列表滚动、badge / Details / Esc、宽窄与 Light / Dark、空态和网络禁用 |

额外真实 SSH 回归验证转发通道空闲七秒后仍可收发和停止，修复依据见 [ADR 0002](decisions/0002-forwarded-channel-keepalive.md)。测试使用独立数据目录、一次性凭据与隔离 OpenSSH，不连接用户实际服务器。

GUI C 已由用户体验确认，覆盖固定 / 默认 Server、端口 / Enter、Local / Remote、高级端点、调整详情和核心操作体验。GUI D 也已由「GUI 测试通过，继续 M9.」确认。Agent 的自动化并不替代人工关卡，也未据此宣称实测系统 High Contrast。M9 的 Server 删除同时覆盖 Quick 的待启动任务、临时隧道和选择清理，详见 [deletion](deletion.md)。
