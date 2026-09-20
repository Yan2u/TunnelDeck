# M6：多规则转发、Local 端口分配与生命周期

日期：2026-09-20。延续 `codex/m0-m1-foundation` 的未提交工作区，保留此前 M0–M5 和用户 `.codex/prompt/260919.md` 改动。用户「GUI 人工评测通过，继续 M6.」记录为 M5 GUI B 通过。

## 目的与实现

让 M7 可以直接接入可脱离 WinUI 运行、已验证真实链路的转发引擎。维持 Local 确认占用才递增、Remote 一次申请且独立传输的范围。

- `Core/Tunnels/TunnelManager`：拆分 Start、Stop、Batch；原子去重与登记，保留首次来源 / Requested，记录 Actual / 调整原因 / 搜索结果。按 ServerId 串行控制，全局限制并发，批量按输入顺序返回部分结果；StopAll / StopServer 建立准入屏障。
- 启动、重复调用者取消、Stop、Remove、Shutdown 保留同一实例的 reservation、connection、handle 和工作任务。迟到句柄必须释放；失败清理保持 Stopping 并允许重试。Remote 先拒绝再关闭传输的竞态不能吞掉原始拒绝分类。
- `Core/Abstractions/ForwardActivity`、`Runtime/ActiveTunnel`：独立记录请求诊断，2 秒聚合重复通知；版本避免并发事件乱序导致计数倒退。请求失败与监听 / 连接失败分开。
- `Core/Connections`：lease 增加 InvalidateAsync 和可重试释放，重试不重复扣减借用者；HostKeyApprovalCoordinator 只合并精确相同的主机指纹确认，不共享 Remote 认证或传输。单个取消不影响其他等待者，最后一个取消排空实际对话框。
- `Infrastructure/Ssh`：SshControlSession 将 worker 登记与传输失效放在同一锁下，取消或超时观察实际 worker 结束；移除可能丢弃后台工作的固定等待退出路径。SshNetSession 在 Start 前拥有候选与订阅；SshForwardHandle 管理请求 / 异常 / Closing 事件；ForwardFailureMapper 固定安全分类与文案。
- `TunnelDeck/Services/AppServices`：暴露 Tunnels，退出顺序 Tunnels → Connections → Configuration。M6 未启用 M7 的转发 UI 命令。

## 测试与问题修正

`./scripts/Test.ps1 -Ssh`：Core 230、Infrastructure 87、真实 SSH 55，**372 / 372 通过，0 跳过**。较 M5 增加 74 个测试结果（含原枚举政策矩阵扩展），其中 20 个为真实转发引擎用例。

Core 覆盖 Local 搜索耗尽 / 边界 / 时间、不可重试错误、去重、部分成功与并发限制、停止准入、保存身份门禁、迟到句柄与清理重试、通知乱序、指纹确认合并与独立取消。Infrastructure 覆盖错误映射和取消 / Dispose 的真实 worker 所有权。

受控 OpenSSH 8.9p1 上传输两种模式的 1 MiB echo、HTTP 及 8 个并发客户端；验证最小可用端口、Local 共享单条停止、不同 Server 的本机端口竞争、三类 Remote 拒绝。MultiConnectionSshProxy 持有最新物理连接的回复，真实服务端已创建监听时分别执行超时、取消、Remove 和 Shutdown，最终无遗留监听，其他传输不受影响。

排查并修正：

1. 传输关闭取消了等待，可能先于适配器返回 Remote 拒绝。现在排空原任务并保留确认过的失败，不被通用断线替代；新增确定性的竞态用例。
2. 本机动态 TCP 范围为 1024–15000。测试使用自动分配端口再释放时，后续 HTTP / SSH 出站连接可能抢占；LocalPortBlock 改用受控的非动态范围并真实持有相邻端口。生产路径仍只做真正绑定。
3. Remote 目标拒绝诊断已正确产生，但测试错误假定客户端立即 EOF。夹具现在明确记录 EOF / reset / timeout，并独立验证请求诊断与同一 listener 的恢复传输。
4. 夹具同时发送 / 接收较大数据，支持有界 HTTP 响应长度和动态 echo 目标，提升 accept backlog 以容纳 8 个并发测试客户端；保留控制进程诊断。先前一次控制 HTTP 响应中断在后续定向与全量运行未重现，未将未经证实的原因归入生产缺陷。

## 构建、原生回归与交付

- `./scripts/Build.ps1 -LockedRestore`：Release x64，0 warnings / 0 errors；保留 WindowsPackageType=None、既有 SDK 和锁文件。
- `./scripts/Verify-Connections.ps1 -NoFixtureBuild`：表单、密码 / 两类私钥、指纹、Remember / 重启、取消 / 导航与身份门禁通过；指纹确认时关闭约 430 ms。
- `./scripts/Verify-Window.ps1`：标题栏、固定侧栏 / 滚动、短窗、重复关闭和响应性通过；首个窗口句柄约 682 ms，带 750 ms probe 的清理约 1012 ms。此数据不冒充生产 SSH 的统一关闭时限。
- Light / Dark 与宽窄连接截图已查看，位于 `artifacts/m6-review`。M5 历史 evidence / history 保留；M6 无新增人工 GUI 关卡，High Contrast 未由 Agent 切换实测。
- `./scripts/Run.ps1` 实际启动普通模式并确认 Dashboard、窗口响应与 M7 命令仍禁用，保留 App 运行。详细启动身份和截图散列见 `docs/evidence/2026-09-20-m6.json`。
- 更新 implementation-plan、UI spec、research、handoff、README、milestones、connections、domain-model、shell 契约；新增 `docs/tunnels.md`。

## 边界与下一步

SSH.NET 2026.0.0 不完整上报 Local direct-tcpip OpenFailed；只记录实际观察到的请求错误，不伪造目标健康。Remote 拒绝不能区分占用 / 服务端政策。网络黑洞下远端释放仍依赖 sshd 断线检测。

下一步 M7：绑定 AppServices.Tunnels，完成 Quick Forward / Active Tunnels 的实际启停、错误恢复、Requested / Actual 和调整详情，随后进行 GUI C。自动重连、资源删除流程与活动持久化仍属于后续里程碑。
