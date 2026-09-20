# TunnelDeck 新会话交接

> 2026-09-20 实施更新：真实仓库位于 `C:\Users\19559\Mp\ai_workspace\temp\TunnelDeck`。M0–M8 已完成，GUI D 已由用户「GUI 测试通过，继续 M9.」确认；GUI A / B / C 保持已确认。M9 已接入资源删除、影响确认与失败重试，GUI E 待用户评测。459 项测试通过（Core 304、Infrastructure 92、真实 SSH 63），Release x64 锁定构建 0 warnings / 0 errors。请读 [里程碑与最新验证](../../docs/milestones.md)、[删除契约](../../docs/deletion.md)、[Profiles 契约](../../docs/profiles.md)、[转发契约](../../docs/tunnels.md) 和 [ADR 0002](../../docs/decisions/0002-forwarded-channel-keepalive.md)。保留当前未提交的 M0–M9 工作区与用户 `.codex/prompt/260919.md` 的改动。

## M9 接入点与下一步

- `AppServices.Deletion` 的 ResourceDeletionService 统一单个 / 全量 Server 和 Profile 删除。先按 ID 计算影响，确认后设置任务门禁、按 ID 固定顺序取服务器身份门禁，再在 ConfigurationService 队列内重新检查、停止和原子提交。相关影响变更返回 NeedsConfirmation，释放门禁后才重新弹窗。
- `Connections.Work` / `ServerWorkRegistry` 在 Quick、Profile 与连接测试等待输入之前登记任务；Server 删除取消并排空相关 lease，其他 Server 的规则仍可继续。成功删除留下 ID 墓碑；M10 重连与 M11 导入必须复用该门禁，不能重用陈旧快照来复活资源。
- Stop 失败或配置保存失败保留整份旧配置，运行状态如实显示已停止 / 仍在清理的结果。成功重试后，将该 Server 所有终态行（包括上一轮失败已停止的行）在 Activity.Flush 成功之后移除；不能提前丢失停止记录。凭据复用 M3 独占 / 共享引用与持久清理重试队列。
- Profile / Rule 删除只改配置，已接受的启动与现有转发保留来源快照；Source 可读，已删除 Profile 不可导航。空 Profile 保留，默认 / Quick / Pinned / Other / Sidebar / 搜索 / 导航通过配置更新统一清理。
- Delete / Delete all 位于 More。单 Server 规则按 Profile 分组，长清单默认折叠并在对话框内滚动；全量仅显示去重摘要，零项隐藏。Cancel 默认焦点与 Enter / Esc 可用。失败 InfoBar 提供 Review and retry，凭据清理失败另有 Retry credential cleanup。
- 关闭顺序为 Deletion → Profiles → Forwarding → Tunnels → Connections → Activity → Configuration，最后在活窗口上等待 DialogService.CloseAsync，再销毁窗口。不要把 Hide 延迟到 Window.Closed 后执行；本轮原生检查发现并修复了该崩溃。M10 仍需完成自动重连、完整关闭错误恢复、硬时限与单实例。
- 新增 Verify-Deletions.ps1（可用 -LayoutOnly）及两个分拆的测试辅助文件。459 项非 GUI 测试及真实删除 UI 闭环已通过，使用隔离目录与一次性 SSH 夹具；原生输入脚本必须顺序运行。最新证据为 `docs/evidence/2026-09-20-m9.json`，历史为 `.codex/history/2026-09-20-m9-resource-deletion.md`。
- GUI E 尚未由用户确认。根据反馈完善后进入 M10，不能把 M9 或 459 项测试通过当成 M13 / 整个首版完成。Agent 未切换系统 High Contrast。

## M8 已实现与复用点

- `AppServices.Profiles` / `Core/Profiles/ProfileRunner` 管理 Start all、单条 Start、Retry failed、Cancel 和窗口级任务；输入从已保存 Profile 捕获，后台事件通过 Shell DispatcherQueue 更新页面。ProfileRunSnapshot / ProfileRuleResult 保留逐条结果与运行 ID，Latest start 不冒充当前活跃数。
- `ProfileEditorModel` / `RuleEditorModel` / `ProfileRuleItem` 是可测试的草稿与投影，`RuleEditorView` 只处理原生输入和 focus。Apply / Move / Remove 不写盘，Save 一次提交；编辑不改变已有运行快照。新 Rule 使用当前草稿的最后一条 Server 作为最高优先级默认。
- ProfileEditPolicy 只比较名称和有序规则，保存保留最新 LastUsedAt，其他资源和活动提交不造成无关冲突；同 Profile 改写或删除会拒绝旧草稿。Pinned 重排只比较捕获的对应顺序，失效提案不覆盖新 Pin / Unpin。
- 一次 Profile 操作内，同 Server 临时凭据合并为一个输入任务，可用于多个独立 Remote 连接；结束后清空引用，不跨操作缓存或保存。ProfileUses / LastUsedAt 在该操作首次新成功时计一次；纯 AlreadyRunning、全失败、无成功的取消和页面访问不计数，写入失败沿用 Activity 的重试队列。
- 资源页使用两个原生 ListView；Pinned 为单选与内部拖排，名称按钮导航，Other 拒绝拖放；保留键盘 Move up/down，写入失败恢复投影并显示 Retry。两类 Sidebar 独立限额，LastUsedAt 不改 Other 数组；同名描述补充短 ID，搜索随配置更新。
- M9 删除协调已覆盖 ProfileRunner 中等待凭据、尚在启动的任务和 Quick 命令，再停止引擎实例并提交配置。Rule Remove 仍是可丢弃的草稿操作。不要绕开批量取消、身份门禁或直接删除运行行。
- M8 新脚本为 Verify-Profiles.ps1 和 Verify-Resources.ps1，后者使用有前台 / 坐标保护的原生 SendInput 拖排。键盘 / 鼠标 GUI 脚本顺序执行，拖动过程中不要反复激活窗口；外部读取测试配置对文件替换期间的短暂缺失 / sharing violation 做有界等待。
- M8 原始证据为 `docs/evidence/2026-09-20-m8.json`，历史为 `.codex/history/2026-09-20-m8-profiles-and-resources.md`。GUI D 现已由「GUI 测试通过，继续 M9.」确认；旧报告中的 Pending 仅是生成时的占位。Agent 没有切换系统 High Contrast。

## M7 已实现与后续接入点

- `AppServices.Forwarding` 的 TunnelWorkspace 已封装 Quick / Stop / StopAll / Retry / Remove，使用稳定 TunnelItem 与 DispatcherQueue，忽略旧运行 / 配置 revision。`AppServices.Tunnels` 提供底层多规则能力，M8 已复用，不把生命周期写进 Page。
- QuickForwardModel 保存跨页草稿与原始端口文本；PortInput 捕获 NumberBox 内部 TextBox，Enter 在 KeyUp 规范化前提交。ComboBox ItemsSource 的临时空选择被忽略，真实资源失效由 Reconcile 处理；初始 focus 等页面与配置均就绪后只执行一次。
- 只有确认的 Local AddressAlreadyInUse 可递增，默认 20 次 / 总 10 秒 / 单次 2 秒；Remote 一次申请、独立传输。页面不能先探测端口、改 Requested 或改写已保存 Rule。
- ServerUseLease 保留身份；SshSessionLease 管理物理传输引用和失效。清理失败保留 Stopping 与所有权，不能先移除行后遗忘句柄。当前关闭顺序先排空 M9 删除事务，再依次处理 Profiles、Forwarding、Tunnels、Connections、Activity、Configuration，最后关闭对话框和窗口。
- 精确相同的指纹并发首连只确认一次；取消一个等待者不取消其他请求。未保存密码且需要新连接时先输入，保存的凭据失效可从安全失败恢复。Quick 临时输入不缓存 / 持久化，新 Remote 连接可能需再次输入；保存入口仍是 Server 的 Remember / Save。
- TunnelActivityRecorder 按运行 ID / 阶段保存 Started / Stopped / Failed / Cancelled，最新 200 条；每个新成功实例增加 TotalForwards / ServerForwards 并更新 LastUsedAt，重复提交不计数。写盘失败保留队列和 Stopped 行供 Retry。M8 已增加 Profile 使用计数，M12 的完整统计不能重复统计已有事件。
- SshSessionFactory 用公开 ConnectionInfo.ChannelRequests 移除 keepalive 注册，让 forwarded-tcpip 通道走未知请求的 CHANNEL_FAILURE 答复路径；严格 sshd 探测下空闲七秒仍可传输。保持客户端 20 秒 keepalive 和夹具 ClientAliveInterval 2 / CountMax 2，依据见 ADR 0002。TCP half-close 与长期通道回收仍待 M10 / M13 评估。
- Local 的 direct-tcpip OpenFailed 未完整透传公共事件，不能承诺每次目标失败都显示诊断；Remote 已观察到的请求错误独立聚合，不销毁仍有效的 listener。两种模式都有真实目标恢复传输测试。
- Quick Forward / Active Tunnels 在普通模式已可实际操作；自动重连留待 M10。未知 schema 仍 State=null 且只读，设计预览在后端也禁止网络。Stress 预览包含 24 行、23 Running，可用来检查独立滚动、badge / Details / Esc 与宽窄布局。
- 验证命令：Build.ps1 -LockedRestore、Test.ps1 -Ssh -NoFixtureBuild；原生脚本 Verify-Forwards、Verify-Dashboard、Verify-Connections、Verify-Window、Verify-Shell、Verify-Storage 全部通过。GUI 键盘脚本顺序运行，保留前台保护。夹具 Python 已重建；后续改动时须去掉 NoFixtureBuild。
- M7 原始证据 `docs/evidence/2026-09-20-m7.json` 和历史 `.codex/history/2026-09-20-m7-quick-forward.md` 保留；GUI C 已确认，不将某个里程碑完成误报成整个首版完成。

以下保留最初规划会话的历史说明，不代表当前代码状态。

本目录是规划资料，不是 TunnelDeck App 源码。用户已经创建空白 WinUI 3 template，但当前规划会话工作区为 `C:\Users\19559\Mp\Python\tools`，未读取或构建那个模板。应在真实模板项目中开始下一次实现。

## 文档阅读顺序

1. [implementation-plan.md](implementation-plan.md)：目标、架构、数据契约、M0–M13 实施/测试/验收。
2. [research-and-decisions.md](research-and-decisions.md)：经官方文档核实的边界及尚未由澄清回复确认的规划默认。
3. [ui-ux-spec.md](ui-ux-spec.md)：用户原始 77 条 UI/UX 规范，仅去掉引用符号，作为视觉和交互基准。

如将此目录复制进 TunnelDeck repo，应整体复制以保留相对链接；源目录绝对路径为：

`C:\Users\19559\Mp\Python\tools\docs\tunneldeck`

## 可以粘贴给新会话的请求

```text
请基于当前 TunnelDeck 空白 WinUI 3 template 开始实现。

先阅读 C:\Users\19559\Mp\Python\tools\docs\tunneldeck 中的
implementation-plan.md、research-and-decisions.md、ui-ux-spec.md，
以及本项目适用的 AGENTS.md。整体目标是“构建 TunnelDeck - 一个 WinUI3 的端口转发助手”。

本轮先完成 M0 和 M1：核对并运行现有模板，建立测试环境，验证 SSH.NET 的
Local/Remote 转发、单条停止、远端拒绝/超时语义、host key 校验和 WinUI 基础窗口能力。
保留现有模板启动/打包模式，不重建一个重复项目，不直接铺开全部页面。

此前未确认的四项规划默认是：
1. 关闭窗口停止所有转发并退出；
2. SSH.NET 直连、密码和私钥、有限 SSH config 导入；
3. 后续用户已确认：仅本地监听端口确认占用时自动递增，Remote 失败不递增；
4. 首版隐藏累计流量，仅做有可靠口径的基本统计。
如果本任务中有我对此作出的更新答复，以更新答复为准。不要把规划默认写成我已确认。

非 GUI 行为由你编写并运行自动化测试，真实 SSH 测试使用隔离夹具。
GUI 变更先 build/run，确认实际窗口，再按对应里程碑给出简短人工评测步骤和通过标准。
不要把尚未获得我的评测标成通过；等待 GUI 反馈期间可以推进无依赖的底层工作。

每次较大改动在 .codex/history 写记录。完成 M0/M1 时交付实际命令、测试证据、
已确认技术边界和阻塞项；后续再按 M2–M13 的依赖逐层推进。
```

## 实施时不能丢失的约束

- Profile 可跨多个 Server；运行实例和配置分离，Requested 与 Actual 分开。
- 同 Server 的 Local 规则共享传输，Remote 每条独立传输，控制请求串行；实际网络数据链路要测试。
- 仅 Local 本地监听占用允许自动递增；Remote 失败返回通用错误，不自动递增。
- SSH `-R` 失败原因不透明；超时可能有副作用，不能一律当端口占用。
- SSH.NET 信任必须显式校验，密码和私钥口令不进 JSON/日志/导出。
- Sidebar 是有限 working set；All 的 Other 顺序不随 Recent 更新而变。
- 删除 Server 先算影响，全部停止成功后才能提交配置删除；失败保留配置，已经停止的运行实例如实显示。
- 原生 WinUI 默认、ThemeResource、Mica、紧凑布局优先；不采纳通用 Web 技能生成的视觉风格。
- 正常关闭清理与异常网络下远端释放的保证不同，不作无法证明的承诺。

## 最初规划时的完成情况（历史）

已完成：规划、原始规范归档、官方资料核实、里程碑测试/验收设计。

尚未执行：模板审计、App 代码、NuGet 安装、SSH 实验、编译、GUI 验收、打包。新会话不得将本文档交付误当成 M0 或 M1 已通过。
