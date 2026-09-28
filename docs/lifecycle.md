# M10 重连、退出与单实例契约

更新：2026-09-20。M9 GUI E 已由用户「GUI 测试通过，继续 M10。」确认。M10 的构建、自动化与原生验证证据见 [milestones](milestones.md)；本页描述 `Core/Lifecycle`、引擎重连分支、关闭流程与单实例的行为约定。

## 自动重连范围

只有**已建立**的转发在断线后进入自动恢复。初次启动失败沿用 M6 / M7 的终态与手动 Retry；Stop、Delete、Exit 期间及之后不复活任何规则。

- 触发：`TunnelFailureKind.Connectivity` 与 `TimeoutUnknownOutcome`。认证、host key 变更、信任库存储错误、远端拒绝、策略与输入类错误直接转为 Failed，等待手动 Retry。
- 默认参数：初始退避 1 秒，指数增长，上限 30 秒，抖动 ±20%，总窗口 2 分钟。窗口耗尽后 Failed，保留最后一次错误。
- 自动恢复只使用已保存凭据或可用私钥，**不缓存临时输入的密码**；未保存密码的 Server 断线后转为 Failed 并提示 Retry。
- 自动恢复**不会弹出主机信任对话框**。未知或变更的指纹按 ChangedHostKey 结束恢复。
- 每台 Server 一个恢复组；组内的 Local 规则共享恢复后的传输，Remote 规则各自独立重新申请。其他 Server 的转发不受影响，继续传输。

`ReconnectCoordinator` 只在 `AppServices` 附加到引擎时启用；M6 的独立引擎夹具保留旧终态行为。

## 运行实例保持不变

重连沿用同一 `ActiveTunnel`：ID、Requested、Source、StartedAt 不变，`Generation` 递增，`ReconnectAttempt` / `NextReconnectAt` 在等待期间可见，成功后 `ReconnectCount` 加一并更新 `LastConnectionAt`。

- Local 从上次 Actual 端口开始重新绑定；只有确认的本地占用且规则允许自动查找时才递增。Remote 只申请原监听端口，失败即 Failed。用户手动 Retry / 重新启动的新实例从 Requested 开始。
- 自动恢复不写 Started 活动记录，不增加 TotalForwards / ServerForwards，不更新 LastUsedAt；等待期间不写配置。
- 请求计数跨代累积；旧代次的迟到回调按 Generation 丢弃。

## Stop、Delete、Exit 与迟到连接

- 等待期间 Stop：取消该规则的恢复票据，其余同 Server 规则继续恢复。
- Server 删除：删除门禁先取消并排空恢复组，再停止实例、提交配置；成功删除留下 ID 墓碑，恢复不会重新引用已删除资源。
- 退出：`ReconnectCoordinator.ShutdownAsync` 禁止新的恢复并显式停止所有 Reconnecting 实例，包括尚未进入恢复组的实例。
- 迟到成功的 Connect 立即被 Stop 回收；未确认的 Remote 申请在 Stop / Delete / Exit 期间保持所有权，直到 sshd 答复或传输释放。真实 SSH 用例覆盖三种动作。
- 网络黑洞：本地监听、传输与运行行先释放（Stop 超时后返回 TimeoutUnknownOutcome）；远端端口要等 sshd `ClientAliveInterval 2 / CountMax 2` 检测到会话失效后才释放。这是已声明的边界，不作更强承诺。

## 关闭流程

所有退出入口（×、Alt+F4、任务栏关闭、重复点击）共用 `MainWindow.Lifecycle`：

1. 第一次 Closing 取消系统关闭，禁用导航 / 搜索 / 命令，显示「Closing TunnelDeck」进度条，`AppServices.BeginClosing` 停止接收新工作。
2. 先取消并排空普通对话框，再以 10 秒预算执行 `ShutdownCoordinator`：Work registry → Import / export file work（M11）→ Deletion → Reconnect → Profiles → Forwarding → Tunnels → Connections → Activity → Configuration（→ 可选窗口 probe）。
3. 每步只执行一次；超时或失败后重试从**未完成的步骤**继续，已接收的清理任务保留并被重试等待。
4. 失败时显示原生 `ShutdownDialog`：Retry 为默认；Secondary 为「Exit without saving」（保存阶段）或「Exit now」（清理阶段），使用危险色。Esc 关闭对话框并进入「Closing paused」，再次关闭即重试。
5. 显式退出选择在关闭窗口后调用 `Environment.Exit(0)`，不依赖 finally；已停止的转发不会因保存失败复活，旧 JSON 保持原样。

正常关闭先在活窗口上取消并排空对话框，再销毁窗口（保留 M9 修复）。

## 单实例

`Program.Main` 为同步 STA 入口，禁用生成的 Main；在打开任何配置 / 凭据之前，按数据目录规范化路径的 SHA256 注册 `AppInstance`。

- 同一目录（含路径别名）的第二次启动把激活转交给主实例并以 0 退出，不获取配置锁，不写配置。主实例还原最小化窗口并激活。
- 显式隔离目录允许并行的独立测试实例；默认目录保持正常凭据命名空间。
- 启动异常显示原生 MessageBox（安全的 HRESULT），不进入半初始化状态。

## 已验证与已知边界

- 100 次真实 Local + Remote 启停循环：会话全部释放、端口可复用；预热后句柄增长 5、线程增长 0、工作集增长约 8 MB，见 `artifacts/m10-review/cycles.json`。这是单次隔离测量，不作性能保证。
- 已验证普通 `sshd` 监听重启**不会**断开已建立会话；夹具区分「仅重启监听」与「终止客户端」两类故障。
- TCP half-close 与长期通道回收仍不在已验证范围，见 [ADR 0002](decisions/0002-forwarded-channel-keepalive.md)。托盘未实现；关闭窗口仍是退出。
