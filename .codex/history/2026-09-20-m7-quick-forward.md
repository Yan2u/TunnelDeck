# M7：Quick Forward 与真实活动列表

日期：2026-09-20。延续 `codex/m0-m1-foundation` 的未提交工作区，保留 M0–M6 和用户 `.codex/prompt/260919.md` 的修改。用户授权「继续 M7.」；GUI A / B 保持已通过，M7 GUI C 待人工评测。

## 目的与模块

将 M6 引擎接入 Dashboard，完成选 Server → 输入端口 → Enter → 真实运行 / 独立停止的核心路径。继续只允许 Local 确认占用时递增，Remote 失败需手动处理。

- `Core/Presentation`：ObservableModel / IViewDispatcher、QuickForwardModel、TunnelItem、TunnelWorkspace 及拆分的 Commands。保留原始文本与跨页草稿、固定 / 默认选择、稳定行对象、模式方向、Requested / Actual、临时凭据恢复和窗口级命令追踪。旧 QuickForwardViewModel 与静态 TunnelRow 移除，详情页剩余显示记录整理到 ResourceDetailRows。
- `Core/Tunnels/TunnelActivityRecorder` 与 `Runtime/ActiveTunnel.EndedAt`：离散 Started / Stopped / Failed / Cancelled、按实例阶段去重、200 条 Recent、可靠启动累计次数与 LastUsedAt、失败提交重试。正常 Stop 在清理与历史提交成功后移除行；请求数和 duration 不写盘。最小活动基础提前到 M7，完整 Profile / 重连统计仍按后续里程碑实施。
- `Controls/PortInput`：原生 NumberBox 内部文本捕获，Enter 在 KeyUp 数值规范化前校验；拒绝小数、越界、指数和无效文本，绝不提交上次有效 Value。
- `Controls/TunnelListItem`：实际端口、状态 / 进度、Stop / Retry / Details / More、紧邻 Local 端口的可聚焦调整 badge、轻量 Details Flyout 与仅在打开时运行的 timer，来源快照和导航可用性分开。
- `DashboardPage`：真实绑定、单端口 / 高级端点、临时错误和历史重试、空态、有界活动 ListView、宽窄重排。修复 ComboBox ItemsSource 替换时误清选择；初始 focus 等 Loaded 和配置就绪后只执行一次，后台事件不抢焦点；关闭 InfoBar 不留空白。
- `Services` / `ShellViewModel`：WinUI dispatcher 适配、原生排队凭据对话框、运行 / 配置通知拆分。关闭顺序 Forwarding → Tunnels → Connections → Activity → Configuration，先取消并排空命令，再清理网络并刷新历史。临时秘密不缓存、不写 JSON / 日志 / Credential Manager。
- `DesignScenario`：使用同一 ActiveTunnel / TunnelItem 投影，Stress 增加至 24 行、23 Running；设计预览仍在 UI 与网络边界禁用真实连接。

## 真实 SSH 问题与修复

GUI 流程发现先前运行的 Remote 在空闲约六秒后被严格 sshd ClientAlive 探测终止。SSH.NET 2026.0.0 注册了 keepalive channel request，但 forwarded-tcpip 通道未处理其事件。SshSessionFactory 在连接前从公开的每连接 ChannelRequests 字典移除该项，让库发送未知 WantReply 请求的 CHANNEL_FAILURE 答复；OpenSSH 据此确认存活。无需反射、修改版本、缩短客户端周期或弱化信任；详见 ADR 0002。

新增真实用例建立通道后空闲七秒，再比较 payload、确认 Running 和正常停止释放。TCP half-close 与长期通道回收没有在本次修改，仍待 M10 / M13 评估。

夹具控制 HTTP 偶发提前结束，改用 HTTP/1.1 和已有 Content-Length，减少 loopback relay 上的连接反复开闭；新增八路并发探测后 100 轮 reserve / listening / release 验证，无重试掩盖。未宣称已证明底层 WSL 原因。

## 验证

- Release x64 锁定还原构建：0 warnings / 0 errors；保留直接 exe、WindowsPackageType=None、既有 SDK、SSH.NET 2026.0.0 和锁文件。
- `Test.ps1 -Ssh`：**404 / 404 通过，0 跳过**（Core 260、Infrastructure 87、真实 SSH 57）。新增 30 项 Core 用例和 2 项真实 SSH / 夹具用例；镜像已重建。
- `Verify-Forwards.ps1 -NoFixtureBuild`：隔离 Server 创建 / Pin / 重启 / 初始 focus；Enter 原始文本校验；N / N+1 占用后 N+2 与真实 payload；重复提交不增实例 / 计数；Details tick 不写盘；草稿跨页；高级 Local、Remote 拒绝后手动重试、独立停止；带真实 Local / Remote 关闭，监听释放且四次成功启动 / 四次正常停止落盘，秘密未写配置。
- `Verify-Dashboard.ps1`：24 行独立滚动、Quick 位置固定、键盘 Details / badge / Esc、1200 / 900 / 640 / 500 epx 的 Light / Dark、预览空态与网络禁用。一次前台保护中止未发送按键；保持同一保护重新运行通过。
- `Verify-Connections.ps1 -NoFixtureBuild`、`Verify-Window.ps1`、`Verify-Shell.ps1`、`Verify-Storage.ps1` 全部回归通过。连接凭据与信任、导航 / 搜索 / Pin / 重启、未知 schema 和恢复提示均保留正确行为。
- 本次单次测量：真实混合转发退出约 232 ms；指纹对话框关闭约 460 ms；带 750 ms probe 的重复关闭约 1229 ms，窗口保持响应；首个句柄约 1346 ms。不作为通用性能承诺。
- 检查最终目标窗口截图，包括调整说明、左侧 Details、真实混合转发和宽窄长列表。Agent 未切换系统 High Contrast；自动化不替代用户 GUI C。

## 文档与交付

新增 `docs/quick-forward.md`、ADR 0002 和 `docs/evidence/2026-09-20-m7.json`；更新 README、milestones、implementation-plan、UI spec、research、handoff、connections、tunnels、domain-model、shell 和 storage 契约。脚本、测试、源码和截图散列记录在 M7 证据，旧里程碑证据保留。

最终普通模式应用供 GUI C 评测使用，启动身份和截图见证据；启动本身不连接用户保存的真实服务器。根据 GUI C 反馈完善后进入 M8。Profile 编辑 / Start all 界面、删除一致性、自动重连、Terminal 和完整统计仍按后续计划实施。
