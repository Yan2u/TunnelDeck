# M8：Profiles、批量启动与资源排序

日期：2026-09-20。用户「GUI 通过，继续 M8.」已记为 M7 GUI C 通过，GUI A / B 保持此前确认；M8 GUI D 待用户评测。延续 `codex/m0-m1-foundation` 的未提交工作区，保留 M0–M7 及用户 `.codex/prompt/260919.md` 改动。

## 目的与实现

让用户保存跨服务器的有序规则组，一次启动、逐条查看结果并只重试失败项；补齐 All 页面与 Sidebar 的资源排序行为。继续维持仅 Local 确认占用才递增、Remote 一次申请且独立传输的范围。

- `Core/Presentation/ProfileEditorModel`、`RuleEditorModel`、`ProfileRuleItem`：页面草稿、单条规则编辑、原始端口文本校验、上一条 Server 默认、Move / Remove / Cancel、稳定运行投影。Apply rule 不写盘，Save 提交整份 Profile；未保存、修改中或空 Profile 不启动。
- `Core/Policies/ProfileEditPolicy`、`Persistence/ConfigurationService.Profiles.cs`：Profile 保存比较名称和有序规则，不因活动或其他资源更新产生全局 revision 冲突；保留最新 LastUsedAt。已被改写或删除的 Profile 拒绝陈旧编辑。重排捕获原 Pinned 顺序并调用 M2 的 ResourceOrderService，拒绝跨组或失效提案。
- `Core/Profiles/ProfileRunner` 及拆分的 Start：窗口级批量任务、逐条不可变结果、同 Profile 并发共用 Task、不同 Profile 的独立取消、只重试仍存在的失败 RuleId。真实调度、去重、句柄及连接生命周期复用 TunnelManager。
- 临时凭据在一次 Profile 操作内按 Server 合并，可供多个独立 Remote 连接使用；结束后清空引用，不持久化或跨操作缓存。host key 仍由现有连接边界精确校验。取消会等待实际工作清理，结果保留已经登记的 TunnelId。
- `TunnelActivityRecorder`：增加 ProfileUses / Profile.LastUsedAt 的离散写入。一次操作至少有一条新实例成功才计一次；纯 AlreadyRunning、全失败、无成功的取消以及页面访问不计数。按 OperationId 去重，失败提交进入同一可重试 FIFO。
- `ProfileDetailViewModel` / `ProfileDetailPage` / `RuleEditorView`：页面内完整表单，单条 Start / Stop、Start all / Retry failed / Cancel start。Latest start 明确表示最近启动结果，各行独立显示当前状态；展开编辑器后定位到编辑区域。离开页面退订 UI 事件，批量命令继续由窗口服务持有。
- `ResourceListView` / `.Drag` / `ResourceListViewModel`：原生 ListView + ObservableCollection，Pinned 单选与内部拖排，名称按钮导航，Other 拒绝拖放。保留键盘 Move up / down，失败恢复当前已保存的投影并提供 Retry；内容相同的普通更新不清空集合。
- `ResourceRow`、`ShellViewModel`、`MainWindow.Navigation`：同名资源的次级描述 / ToolTip 补充短 ID；已有搜索随持久配置刷新，导航仍用 Guid；Sidebar 两类独立限额且 LastUsedAt 不改 Other 顺序。
- `AppServices`：关闭顺序 Profiles → Forwarding → Tunnels → Connections → Activity → Configuration，先排空批量 / 凭据工作，再释放网络并保存活动。

## 验证

Core **283**、Infrastructure **87**、真实 SSH **60**，合计 **430 / 430 通过，0 跳过**。相对 M7 增加 23 项 Core 与 3 项真实 SSH 测试。Release x64 锁定还原构建 0 warnings / 0 errors；保留直接 exe、WindowsPackageType=None、既有 SDK、SSH.NET 2026.0.0 与锁文件。

新增 Core 用例覆盖规则默认 / 校验 / 取消、规则身份与顺序、内容冲突与无关更新、写盘失败重试、Pinned 的捕获顺序和跨组拒绝、逐条部分成功与重试、重复启动、批次临时凭据、取消后的迟到句柄、其他 Profile 的独立等待者、使用次数与预览门禁。

真实 Profile 用例验证两台逻辑 Server 的 Local / Remote payload、重复 Start all、单条停止后的其他传输；拒绝转发的服务器恢复后仅启动失败规则；代理持有真实远端确认时取消，远端监听释放且已成功 Local 继续传输。原 M6 / M7 的大 payload、并发、端口分配、信任、迟到清理与七秒空闲通道回归继续通过。

新增 `Verify-Profiles.ps1`：原生草稿 / Apply / Cancel / Save、默认服务器、Remote 关闭自动搜索、两次按 Server 输入、指纹确认、跨服务器部分成功、仅失败项重试、纯重复不计数、修改后旧端点仍可传输、独立 Stop、宽窄 / Light / Dark、关闭清理及重启配置。

新增 `Verify-Resources.ps1`：Profiles / Servers 原生鼠标拖排、键盘菜单移动、跨列表拒绝、Pin 追加 / Unpin 到 Other[0]、真实文件锁造成的保存失败 / 回退 / Retry、空 Pinned、资源新增 / 改名 / 搜索、Sidebar 上限与重启顺序。鼠标驱动使用实际 UIA 边界、SendInput 和前台 / 按键 / 坐标保护；不在原生拖动中反复激活窗口。外部测试读取对文件替换中的短暂缺失和 sharing violation 做有界等待，生产存储仍用原有锁与原子替换。

最终原生回归报告、截图、源码 / 构建 / TRX 散列和普通模式启动身份汇总于 `docs/evidence/2026-09-20-m8.json`。旧脚本中的人工评测 Pending 是占位，不撤销已确认的 GUI A / B / C；Agent 未切换系统 High Contrast。

## 交付与后续

新增 `docs/profiles.md`；更新 README、milestones、implementation-plan、UI spec、research、handoff、quick-forward、connections、tunnels、domain-model 与 shell 契约，并记录 M7 GUI C 的用户确认。

最终普通模式 App 供 GUI D 评测，启动本身不连接真实服务器。根据 GUI D 反馈完善后进入 M9：删除影响与业务一致性。M9 必须同时处理 ProfileRunner 等待凭据 / 启动的任务、Quick 和现有隧道，不能只扫描 Running。当前 Profile / Server 删除尚未启用；Rule Remove 是可取消的配置草稿操作，已有运行实例保留。

自动重连、导入导出、Terminal、完整设置 / 统计和长期通道回收仍属于后续里程碑；本轮不扩展 SSH.NET 的 Local 目标错误观测或远端失败分类能力。
