# TunnelDeck 新会话交接

四份根文档 `README.md`、`README-zh.md`、`CONFIGURATION.md`、`CONFIGURATION-zh.md` 已同步此次脚本目录整理；新增语言互链，使用实际小写 `.md` 文件名，中文版 Profile 术语统一为“配置组”。配置说明区分历史 ZIP 与当前源码，标注 schema 2 发布元数据 / smoke 断言尚待适配。104 处本地链接（含大小写）和中英文命令一致性检查通过。

## 发布包脚本目录整理：2026-09-28

根 `packaging/` 中的 `Check-Prerequisites.ps1`、`Smoke-Test.ps1` 已原样移到 `scripts/packaging/`，原空目录已移除。`Publish-Release.ps1` 的复制源已更新，发布包根目录仍保留这两个同名脚本及由 `docs/release.md` 生成的 README；包内命令和 `Verify-Release.ps1` 调用不变。两脚本散列一致；57 个 PowerShell 脚本语法、136 处静态依赖、三条实际发布复制语句和只读运行库检查通过。未重新发布或运行 GUI smoke；既有 schema 2 smoke 断言仍需在当前版本发布前适配。详见 [配套脚本说明](../../scripts/packaging/README.md) 与 [整理记录](../history/2026-09-28-packaging-script-organization.md)。

## SSH 夹具目录整理：2026-09-28

原 `test-infra/Start-SshFixture.ps1`、`Stop-SshFixture.ps1` 和 `sshd/` 的 Dockerfile / Python 服务移至 `scripts/fixtures/`。`scripts/Test.ps1`、相关 debug 脚本、清理提示和当前文档已更新。`tests/` 为 C# 测试项目，不含独立脚本；`test-infra/TunnelDeck.ProcessProbe/` 仍为集成测试引用的编译型辅助程序，两者保留原位。迁移后的真实 Docker 构建、夹具启动 / 健康检查 / 停止及凭据清理通过；55 个 PowerShell 脚本语法与 136 处静态依赖检查通过。详见 [夹具说明](../../scripts/fixtures/README.md) 与 [整理记录](../history/2026-09-28-fixture-script-organization.md)。

## 项目 README 与截图：2026-09-28

根 README 改为英文项目简介，包含核心概念、功能、模块、构建入口、两张截图和实际依赖。此前完整说明迁到根目录 `CONFIGURATION.MD`。`assets/screenshots/dashboard.jpg`、`profile.jpg` 均为 1072 × 740，来自独立设计预览实例，无真实连接或用户凭据；截图实例已关闭。修复 `.gitignore` 的 `[Dd]ebug/` 对 `scripts/debug/` 的误匹配，47 个脚本及目录说明现在可被 Git 收录。详见 [本轮记录](../history/2026-09-28-readme-and-screenshots.md)。

## 脚本目录整理：2026-09-28

`scripts/` 一级保留 Build、Run、Test、Publish-Release、Clean-GeneratedArtifacts 和 Enter-DevelopmentShell 六个入口；47 个验证、评测、性能及辅助脚本移到 `scripts/debug/`。脚本调用、仓库根目录、SSH 夹具、符号缓存和当前文档路径已同步；`Publish-Release.ps1 -Verify` 使用 `debug/Verify-Release.ps1`。完整用途见 [脚本目录](../../scripts/README.md)。本次仅调整路径，53 个脚本内容已核对，语法和静态依赖检查通过，未重跑 GUI / SSH 或打包。部分旧脚本仍含早期 UI / schema 断言，重跑时需先适配；历史 evidence / history 里的原路径不改写。详见 [整理记录](../history/2026-09-28-script-organization.md)。

## 当前状态：2026-09-27 多语言与分支整理

已加入 English / 简体中文，设置 → 外观 → 语言 保存后即时切换，不重建窗口、不跟随系统语言。修复了用户反馈的侧边栏、搜索占位符、Usage / No usage yet 及 Add / Import / Export 遗漏，并解决 ComboBox 缓存所选项旧文字的问题。实现使用 Core `.resx` 资源、显式 culture 和 WinUI `LocalizedStrings.Values` 单向绑定；详见 [多语言](../../docs/localization.md) 与 [本轮记录](../history/2026-09-27-localization-and-master.md)。

内部配置现为 schema 3，增加必填 language；内置严格 1 → 2 → 3 迁移，旧数据加载不写盘，首次实际编辑保留备份。Export / host-key / credential-cleanup 仍为 v1。Core 441、Infrastructure 154，共 595 项通过；最终 Release x64 锁定构建零警告、零错误。实机已确认中英文来回切换、现有导航 / 搜索 / 选项、两类资源菜单、导入说明与重启恢复中文。用户桌面输入干扰后未继续窄窗口与 Quick 草稿往返操作，不将它们记为通过；不重复全套 SSH / 系统显示回归。验证摘要见 [证据](../../docs/evidence/2026-09-27-localization.json)。

当前只保留本地 `master`。已将 master 快进到原 foundation HEAD 后删除 `codex/m0-m1-foundation`，551 个文件路径、文件内容、暂存区和工作区状态前后核对一致；历史提交与所有未提交工作都保留，没有 commit / reset / clean / stash。另一个 M13 detached worktree 保持原状。不得覆盖用户 `.codex/prompt/260919.md`。

最终隔离测试实例使用 `.test-output/localization/smoke`，保留为中文供人工检查。9 月 23 日 `artifacts/release` 发布 ZIP 早于多语言，本轮未覆盖；最新功能位于源码和 Release 构建目录。

## 历史状态：2026-09-23 发布收尾

用户已认可功能与主要 UI / UX，本轮恢复代表性回归并交付 `artifacts/release/TunnelDeck-1.0.0-win-x64.zip`。ReleaseX64 发布配置与 `Publish-Release.ps1 -Verify` 已验证；Core / Infrastructure 共 576 项通过，原生功能、布局、拖放及最终包检查见 [收尾记录](../../docs/ui-refresh-validation.md)。

内存优化减少隐藏控件和重复行；最终包样例空闲 / 20 条转发 Private Bytes 为 143.05 / 202.01 MiB。导航场景的整进程内存没有稳定下降，不可宣称大幅改善，详见 [性能](../../docs/performance.md)。完整转储定位并修复了规则对话框 Opened 早于模型绑定的崩溃；构造时显式初始化绑定，未保留诊断代码。

生成日志、截图、转储、夹具、临时工具及 bin / obj 在验收后清理；最终发布包与 docs/evidence 保留。开发运行前需要重新构建，发布包可直接使用。源码和历史未提交改动均保留；不要恢复旧 UI 目录或覆盖用户 `.codex/prompt/260919.md`。以后按改动选择必要回归，不默认重复已经完成的长时间 / 系统显示设置检查。

以下为历史交接，当前状态与验证边界以本文件最上方多语言记录为准。

> 后续验证方式：用户明确要求在当前 UI / UX 剧烈重构阶段大幅精简自动化，等待设计稳定后再逐步恢复全量测试。默认只做构建、启动和本次变更必要的少量检查，不再因视觉迭代自动运行全套回归。`Verify-Resources.ps1 -SmokeOnly` 可单独检查一次卡片拖放。

> 最新任务（2026-09-22）：用户确认功能总体可用，授权保持功能不变的 UI / UX 重构。当前 UI 工程已迁至 `src/TunnelDeck`，设计、进展和验证分别见 [设计规则](../../docs/ui-design-system.md)、[本轮历史](../history/2026-09-22-ui-design-foundation.md)、[验证](../../docs/ui-refresh-validation.md)。本轮覆盖对话框、Dashboard、卡片列表及右键菜单、DevWinUI 设置页和导入导出。下方 M13 记录是此前交付，未验证环境项仍保留。

> 2026-09-21 实施更新：真实仓库为 `C:\Users\19559\Mp\ai_workspace\temp\TunnelDeck`。M0–M12 / GUI A–H 已确认，用户已授权继续 M13。663 项测试通过（Core 428、Infrastructure 148、真实 SSH 87），原生流程与性能证据见 [M13 验证](../../docs/m13-validation.md) 和 [性能](../../docs/performance.md)。导入异常已从 PID 49444 原始转储定位到 GettingFocus 中重置 ListView.ItemsSource，真实错误消息是 “Focus cannot be moved while getting or losing focus.”；已拆开表单与只读预览并保留虚拟化。关闭 RTSS 不是充分规避条件。文字缩放、无障碍、最终产物和人工验收仍按 M13 记录收尾。工作区保留 M10–M13 的未提交改动；不要覆盖用户 `.codex/prompt/260919.md`。

## M12 接入点与下一步

- `AppSettings` 新增默认认证方式与默认密钥路径。`ServerDefaultsResolver` 仅检测 `%USERPROFILE%\.ssh\id_rsa`，显式设置的路径优先；密码默认不复制密钥。新 Server 草稿与无 IdentityFile 的 SSH config 条目使用默认值，已有 Server / 草稿 / JSON 显式认证不变。
- 有效的显式 `IdentityFile` 原本已支持，继续优先于全局默认。显式 none / 多路径 / 无效路径沿用明示密码回退；无 IdentityFile 而全局默认密钥无效时预览要求先修正 Settings，不默默改成密码。提交前重新检查密钥文件。
- Settings 的认证选择即时保存；Private key 才显示密钥项。Browse 保存所选文件，手输用 Enter / Save path 保存，空值恢复自动检测；保存失败可 Retry。页面事件拆入 `SettingsPage.Authentication.cs`。
- 内部 `AppState.CurrentSchemaVersion` 现为 **2**；`AuthenticationDefaultsMigration` 严格检查旧 Settings 后补 Password / null，加载不写盘，第一次编辑才保存新格式并保留 schema 1 备份。旧程序见 schema 2 只读。**TunnelDeckExport、host-key 和 credential-cleanup 格式仍为 v1**，不要连带升级。
- 新验证入口 `Verify-AuthenticationDefaults.ps1`，评测入口 `Show-AuthenticationReview.ps1`。原生 Add 按钮动态 ID 为 `AddServer`；默认认证 ComboBox 使用 Home / End 后等待保存。系统 Open / Save picker 分别可能使用 1148 / 1001，必须在已确认 owner 的 HWND 中选择唯一可见 Edit，不能把地址工具栏当作文件名。
- `Core/Presentation/ActivityTimeFormatter` 使用传入时钟值、时区和 culture，显示相对时间及含 UTC offset 的完整时间。`RecentActivityItem` 保留来源快照和 Requested / Actual，标明资源删除 / 改名，同名来源补充 ID。
- `UsageStatisticsProjection` 保留全部历史 Total forwards；Most used 只在当前资源且次数大于零的项中排名，按次数和 ID 稳定选择，显示次数。删除最高项不会挡住剩余资源，计数不改变 Other 顺序。
- Dashboard 默认 5 条 Recent，可展开最多 200 条，使用有高度上限的原生 ListView；每分钟只更新时间文字，卸载 / 离页停止计时。ActivityRecorder 按事件 UTC 时间保留最新 200 条，迟到旧事件仍更新累计计数，不挤掉新历史。
- `ConfigurationService.UpdateSettingsAsync` 在 FIFO 锁内应用字段更新，相同值不写盘 / 不提升 revision。SettingsViewModel 提供失败提示和重试；现有 Quick / Rule 草稿不被全局默认值覆盖。
- 用户修订后的 SSH config：仅必填连接字段缺失 / 非法时不可用。未支持选项、Include / 代理和 Match 条件块忽略并提示；无效可选 IdentityFile 明示密码认证回退。JSON 仍严格全量校验。Import 页在无可提交资源时显示具体说明。
- 原生入口 `Verify-Activity.ps1`、`Show-ActivityReview.ps1` 使用隔离数据、200 条样例活动和 .example 主机。测试输入先聚焦，最小导航模式先打开 pane。SSH 重连测试等待旧句柄清理和恢复组登记，不能只看到 Reconnecting 就立即读取组数。
- M12 GUI H 已由用户「GUI 测试通过，继续 M13.」确认；M13 进行中，当前汇总见 [M13 验证](../../docs/m13-validation.md)。
- 历史缩窗失败及 WER 保留于 `artifacts/m12-auth-defaults/transfer` / `transfer-rerun`。M13 从关闭 RTSS 后的 PID 49444 转储找到真实焦点异常，修正及验证见 [本轮历史](../history/2026-09-21-m13-import-focus-reentrancy.md)。不能再把该错误仅按 `E_SURFACE_CONTENTS_LOST` 名称归为图形故障。

## M11 接入点与下一步

- 用户已确认 MobileGym 访问失败源于服务器配置填写错误，修改后转发正常，该问题闭环。保留新增的两项真实 SSH 回归，不修改转发默认地址；见 [转发排查历史](../history/2026-09-21-quick-forward-investigation.md)。M11 GUI G 已通过，见 [GUI G 验收](../history/2026-09-21-m11-gui-accepted.md)。
- Core/Transfer 定义独立的 TunnelDeckExport v1、ExportBuilder、ImportPlanner、SshConfigParser 和 ResourceImportService。不要把 AppState 当作可移植导出；秘密、凭据引用、信任、统计和运行状态均排除。
- JSON 全量校验后给所有资源 / 规则新建 ID；资源顺序和跨服务器引用重新映射。已存在同名或同 endpoint 资源保持独立。缺失私钥路径使 JSON 整批不可导入；SSH config 现按用户修订忽略无效可选 IdentityFile，并明示密码认证回退。
- 配置提交走 ConfigurationService.UpdateAsync 的 FIFO 队列和预览 revision；在事务内检查 ServerWorkRegistry.RequireResourceAdmission。只追加新身份，不复用删除墓碑。导入后 Delete all 需要重新确认；预览后删除使预览失效。
- Infrastructure/Transfer 提供严格 UTF-8 JSON 编解码和原子文件写入；ResourceTransferFiles 跟踪 I/O，BeginClosing 取消任务，Shutdown 在网络清理前等待其结束。发布边界之后不能误报失败。
- SSH config 支持具体 Host / 静态模式默认、HostName、明确 User、Port、单一 IdentityFile 和 first-value。必要连接字段有效时允许导入，其他不能解析的选项忽略并提示行号；不执行 ssh -G 或配置命令解析用户文件。受控静态兼容测试才调用系统 ssh -G。
- App 增加 ImportPage、ExportPage；All 页 More 入口，虚拟化 ListView 带可滚动页头，底部动作固定。浏览 / 保存使用原生文件选择器。系统 picker 属于 broker 进程，UI 自动化必须按 owner 找 HWND；UIA SetValue 后需要真实编辑事件提交文件名。
- TerminalCommandBuilder 使用 ArgumentList、系统 OpenSSH、-F none 和明确连接参数。Windows Terminal 缺失 / 启动失败，或参数含分号 / 双引号时，直接使用 SSH 控制台，避免 wt 二次解析。外部终端从不获得应用秘密和信任，关闭任一侧不影响另一侧。
- `test-infra/TunnelDeck.ProcessProbe` 为 argv 接收测试程序，只记录参数不执行；Infrastructure 测试会自动构建并复制它。真实 SSH 测试使用一次性密钥及独立 known_hosts；本机 ssh-keyscan 9.5 的 sntrup 问题由已验证连接取公钥的测试夹具避开，产品不依赖 ssh-keyscan。
- `scripts/debug/Verify-Transfer.ps1` 为 M11 原生验证；五套回归脚本新增可选 OutputDirectory，避免覆盖旧里程碑证据。所有原生脚本顺序运行，保留前台所有权保护。
- M11 GUI G 已由用户「GUI 测试通过……可以继续 M12.」确认；后续导入规则修订和 M12 辅助区使用 GUI H 评测。发布与全面性能 / DPI / 无障碍仍属 M13。

## M10 接入点（已验收）

- `Core/Lifecycle/ReconnectCoordinator` 只在 `AppServices` 附加时启用，每台 Server 一个恢复组，票据走 `ServerWorkRegistry`（`ServerWorkKind.Reconnect`）。只恢复已建立的转发，只对 Connectivity / TimeoutUnknownOutcome 重试；认证 / host key / 信任库 / 远端拒绝转为 Failed 等待手动 Retry。默认 1 s → 30 s 指数退避、±20% 抖动、2 分钟窗口。自动恢复不弹主机批准、不缓存临时密码。
- `TunnelManager.Reconnect` 保留同一 `ActiveTunnel`（ID / Requested / Source / StartedAt），Generation 递增；Local 从上次 Actual 起绑定，仅确认占用递增；Remote 只申请原端口。恢复不写 Started 活动、不计数。`ReconnectAsync` 把票据取消直连 `StopAsync`，勿改回延迟停止，否则迟到连接会复活监听。
- `ShutdownCoordinator` 按命名步骤执行且不重复已完成步骤：Work registry → Deletion → Reconnect → Profiles → Forwarding → Tunnels → Connections → Activity → Configuration。`MainWindow.Lifecycle` 以 10 秒预算执行，失败弹 `ShutdownDialog`（Retry 默认 / Exit without saving 或 Exit now 危险色），Esc 进入「Closing paused」；显式退出在 Close 后 `Environment.Exit(0)`。正常关闭仍先排空对话框再销毁窗口。
- `Program.Main` 为同步 STA，禁用生成入口，在打开配置 / 凭据前按数据目录哈希注册 `AppInstance`；第二实例转交激活并以 0 退出。`Run.ps1` 假设每次启动都会出现窗口，测试第二实例请直接 `Start-Process`。默认目录保持正常凭据命名空间，勿把默认启动标为 isolated。
- `Infrastructure/Ssh/SshControlSession.DisposeClient` 对 SSH.NET 2026.0.0 Remote 传输失败瞬间的 Dispose 做二次释放；`SshNetSession` 断线路径先完成客户端释放再释放句柄。移除这些会重新出现间歇性的 Remote 清理竞态。
- 测试：`ResourceCycleTests`（100 次真实循环，写 `cycles.json`）、`RecoveryForwardingTests`、`RecoveryCancellationTests` 需要 `TUNNELDECK_LIFECYCLE_EVIDENCE` 指向输出目录才写证据。夹具新增 `stop` 路由与 `restart { disconnectClients }`；普通监听重启不会断开已建立会话。
- 原生脚本：`Verify-Lifecycle.ps1 -NoFixtureBuild`（可 `-InstanceOnly`）加载 `Lifecycle-TestSupport.ps1` 与 `Lifecycle-ForwardingChecks.ps1`。共享 `Desktop-TestSupport.ps1` 的键盘辅助现在会附加前台线程输入再切前台，仍保留所有权检查；原生脚本必须顺序运行，被其他窗口抢前台时重跑而不是关掉保护。
- 证据：`docs/evidence/2026-09-20-m10.json`、`artifacts/m10-review/`；M9 的 `artifacts/m9-review` 快照不要覆盖。历史：`.codex/history/2026-09-20-m10-reconnect-shutdown-instance.md`。
- GUI F 已由前一会话用户「GUI 测试通过，继续 M11.」确认，见 M10 GUI 历史记录。M11 已接入同一准入门禁。托盘未实现，TCP half-close / 长期通道回收未验证，不要把当前里程碑通过写成整个首版完成。

## M9 接入点与下一步

- `AppServices.Deletion` 的 ResourceDeletionService 统一单个 / 全量 Server 和 Profile 删除。先按 ID 计算影响，确认后设置任务门禁、按 ID 固定顺序取服务器身份门禁，再在 ConfigurationService 队列内重新检查、停止和原子提交。相关影响变更返回 NeedsConfirmation，释放门禁后才重新弹窗。
- `Connections.Work` / `ServerWorkRegistry` 在 Quick、Profile 与连接测试等待输入之前登记任务；Server 删除取消并排空相关 lease，其他 Server 的规则仍可继续。成功删除留下 ID 墓碑；M10 重连与 M11 导入必须复用该门禁，不能重用陈旧快照来复活资源。
- Stop 失败或配置保存失败保留整份旧配置，运行状态如实显示已停止 / 仍在清理的结果。成功重试后，将该 Server 所有终态行（包括上一轮失败已停止的行）在 Activity.Flush 成功之后移除；不能提前丢失停止记录。凭据复用 M3 独占 / 共享引用与持久清理重试队列。
- Profile / Rule 删除只改配置，已接受的启动与现有转发保留来源快照；Source 可读，已删除 Profile 不可导航。空 Profile 保留，默认 / Quick / Pinned / Other / Sidebar / 搜索 / 导航通过配置更新统一清理。
- Delete / Delete all 位于 More。单 Server 规则按 Profile 分组，长清单默认折叠并在对话框内滚动；全量仅显示去重摘要，零项隐藏。Cancel 默认焦点与 Enter / Esc 可用。失败 InfoBar 提供 Review and retry，凭据清理失败另有 Retry credential cleanup。
- 关闭顺序为 Deletion → Profiles → Forwarding → Tunnels → Connections → Activity → Configuration，最后在活窗口上等待 DialogService.CloseAsync，再销毁窗口。不要把 Hide 延迟到 Window.Closed 后执行；本轮原生检查发现并修复了该崩溃。M10 仍需完成自动重连、完整关闭错误恢复、硬时限与单实例。
- 新增 Verify-Deletions.ps1（可用 -LayoutOnly）及两个分拆的测试辅助文件。459 项非 GUI 测试及真实删除 UI 闭环已通过，使用隔离目录与一次性 SSH 夹具；原生输入脚本必须顺序运行。最新证据为 `docs/evidence/2026-09-20-m9.json`，历史为 `.codex/history/2026-09-20-m9-resource-deletion.md`。
- GUI E 已由用户确认（「GUI 测试通过，继续 M10。」）。Agent 未切换系统 High Contrast。

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

~~~text
请继续 C:\Users\19559\Mp\ai_workspace\temp\TunnelDeck 的开发。
先阅读项目 AGENTS.md、.codex/docs/handoff.md、implementation-plan.md，
以及 docs/milestones.md、docs/transfer.md、docs/terminal.md。

M0–M11 和 GUI A–G 已验收；M12 已实现，最新验证与 GUI H 状态请查 docs/milestones.md。
继续已授权的 M13，完成 `docs/m13-validation.md` 中未结束的检查，再交付最终人工 GUI 评测。无需再次请求是否开始 M13。
工作树有 M10 收尾、M11 和 M12 的未提交改动，请保留并核对，不要重置或覆盖。

协议、解析、文件原子性和进程参数由 Agent 测试；GUI 人工只评测交互。
每次较大改动写 .codex/history，验证证据保存到新的里程碑目录。
保留用户 .codex/prompt/260919.md。不要连接用户真实服务器做自动测试。
~~~

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
