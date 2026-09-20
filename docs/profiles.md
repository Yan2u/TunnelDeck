# M8 Profiles、资源顺序与 working set

更新：2026-09-20。M8 GUI D 已由用户「GUI 测试通过，继续 M9.」确认，GUI A / B / C 保持已确认。本文定义 Profile 编辑、批量命令和资源列表契约；当前验证状态见 [milestones](milestones.md)，删除流程见 [deletion](deletion.md)。

## Profile 与规则草稿

Profile 是规则的有序集合，每条规则单独引用 Server。`ProfileEditorModel` 持有页面草稿和打开时的原始 Profile；`RuleEditorModel` 在页面内编辑一条规则，原生 NumberBox 复用 M7 的 `PortInput` 捕获未提交的原始文本。规则编辑区不使用 Flyout，也不为每一行常驻完整表单。

新增规则按当前草稿的上一条 Server → Quick 当前选择 → Quick 固定选择 → Settings 默认 → 空选择决定默认服务器。端点默认 `127.0.0.1`，端口由用户填写；重复添加不要求重新选同一服务器。编辑保留 RuleId，Local / Remote 各自验证监听地址和目标地址，只有 Local 保存自动找本地端口开关。

Apply rule 只改变 Profile 草稿，Cancel rule 丢弃当前规则编辑。Move up / down 和 Remove rule 同样只改变草稿；Profile Save 一次提交名称、规则和顺序，Profile Cancel 丢弃整份草稿。空 Profile 可以保存，不能启动；新建、已修改或仍在编辑规则的 Profile 必须先保存后启动。编辑和删除已保存规则不会停止或改写既有转发，运行实例继续使用启动快照。

`ConfigurationService.SaveProfileAsync` 比较原始与当前的可编辑内容（Id、Name、按顺序的 Rules），忽略 LastUsedAt。后台启动 / 使用统计、主题、其他资源的提交不会制造无关冲突；保存合并当前 LastUsedAt。相同 Profile 被改写或删除时拒绝陈旧草稿，不覆盖其他编辑，也不重新创建已删除资源。候选仍经过 AppState 校验与原子持久化，失败保留原配置。

## 窗口生命周期的批量启动

`AppServices.Profiles` 提供无 WinUI 依赖的 `ProfileRunner`：StartAllAsync、StartRuleAsync、RetryFailedAsync、CancelAsync 和 ShutdownAsync。调用只读取已保存 Profile，由 ProfileSnapshotFactory 复制规则、服务器和来源；离开页面不会遗弃或取消已接受的操作。

同一 Profile 的同时启动共用正在执行的 Task；不同 Profile 可以并行，真实监听的调度、去重、取消与清理由 TunnelManager 执行。每条结果保留 RuleLaunchSnapshot、Outcome、TunnelId 和安全错误，逐条发布带 revision 的不可变 ProfileRunSnapshot。Latest start 区分 Started / AlreadyRunning / Failed / Cancelled，表示最近启动结果；各行另行显示当前运行状态。成功项不因另一条失败而回滚。

Retry failed 只重新捕获当前仍存在的失败 RuleId，保留其他未变化规则的已有结果。完全重复的 Requested 请求复用现有实例，包括不同 Profile 中的同一请求；首次创建者的来源快照保持不变。Stop 是规则级实例停止，没有隐含的 Profile 引用计数。配置行依据 Requested 映射当前实例，并提供 Start / Stop；Dashboard 可查看所有实例及 Requested / Actual 详情。

取消只影响该次尚未完成的启动，已经成功交付的规则保持运行。管理器等待迟到的监听句柄被释放，不能仅取消外层等待就报告完成；取消一份 Profile 不会取消另一份 Profile 对重复请求的独立等待者。取消结果保留已经登记的 TunnelId，便于后续重试与清理。

## 临时凭据与关闭

同一批次、同一 Server 共享一次临时凭据输入，包括该 Server 的独立 Remote 连接。输入只在当前操作内使用；操作结束清空引用，不写配置、Credential Manager、运行快照或日志。取消某个输入只取消相应规则，其他服务器可以继续。保存的凭据失效时仍通过安全失败结果恢复，输入一次后再次失败会保留结果，不循环弹窗。

本次之外的新操作仍按既有凭据与可用连接决定是否需要输入；完全重复的运行请求和已有 Local 传输可直接复用。host key 的指纹校验和确认合并沿用 M5 / M6；预览在命令服务和 UI 两层禁止真实网络操作。

正常关闭顺序为 **Profiles → Forwarding → Tunnels → Connections → Activity → Configuration → 可选窗口 probe**。先取消和排空 Profile 命令及其凭据对话框，再停止 Quick 命令与真实监听，最后保存离散活动和配置。页面退订自身事件，Shell 的批量通知经 DispatcherQueue 发布并读取当前快照，旧通知不会覆盖新状态。

## Profile 使用次数

M7 的 TotalForwards / ServerForwards 继续按新成功运行实例计数。M8 增加 ProfileUses 与 Profile.LastUsedAt：每次 Profile 启动操作（Start all、单条 Start 或 Retry failed）至少有一条新实例成功，使用次数加一；同批其余新成功规则不重复计数。

纯 AlreadyRunning、空集合、全失败或尚无成功项的取消不计数；部分成功后取消仍计一次。重试失败项后产生新成功实例，属于新的有效使用。打开详情、导航、Pin / reorder 和启动后的请求流量不计数。

Profile 使用更新进入同一 TunnelActivityRecorder FIFO 队列，与离散转发事件一起持久化。按 OperationId 去重，写盘失败保留可重试队列，不中断已运行隧道。Profile 保存保留并发更新的使用时间；Other 顺序数组不会被 LastUsedAt 改写。

## 资源列表与搜索

Profiles / Servers 共用原生 ResourceListView：固定 CommandBar，正文由一个 ScrollViewer 拥有滚动；Pinned 与 Other 使用两个 ListView，Other 没有 header。仅整个集合为空时显示空态；Pinned 为空时标题和下方间距一起折叠。

Pinned 使用可变 ObservableCollection 支持原生拖排；拖动开始保存原顺序，只有同一控件的内部 Move 可以提交。Other 不接收拖放，跨列表拖动不触发 Pin / Unpin。菜单的 Move up / down 提供键盘等效操作。所有顺序变更通过 ResourceOrderService：Pin 追加 Pinned 尾部，Unpin 和新增资源插入 Other[0]。

控件依据：[Microsoft Learn 拖放](https://learn.microsoft.com/en-us/windows/apps/develop/data/drag-and-drop)、[ListViewBase.CanReorderItems](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.listviewbase.canreorderitems)。使用 AllowDrop / CanReorderItems / CanDragItems 与原生集合事件，没有重写控件模板或自制拖拽层。

保存重排时核对捕获的 Pinned 顺序，而非全局 revision；其他资源或使用时间更新不产生无关冲突。列表已被其他操作改动时拒绝旧顺序；写入失败恢复当前已保存投影，显示 Retry。重试保留原提案和前置顺序，再次冲突时要求对当前列表重排。普通配置通知在行内容相同时不清空集合。

Sidebar 的 Profiles / Servers 各自遵守 MaximumItems：按 Pinned 顺序优先，再由 LastUsedAt 补足并去重，All 入口常驻。完整资源列表的 Other 始终按持久数组呈现，不跟随访问或最近使用重排。

同名 Profile、名称与 endpoint 都相同的 Server 在次级描述中增加短 ID；选择、导航、排序仍使用完整 Guid。搜索接入当前持久资源，名称改变或资源失效后刷新已有查询的结果，导航继续核对当前 ID 是否存在。

## 验证与后续

```powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Test.ps1 -Ssh -NoFixtureBuild
./scripts/Verify-Profiles.ps1 -NoFixtureBuild
./scripts/Verify-Resources.ps1
```

| 计划用例 | 覆盖 |
| --- | --- |
| PROFILE-01 | 跨服务器 Local / Remote 真实 payload；服务器拒绝后保留成功项、只重试失败规则 |
| PROFILE-02 | 同 Profile 并发共用任务、后续重复启动 AlreadyRunning；迟到句柄 / 真实远端确认待回复时取消；另一 Profile 的重复等待者不被取消 |
| PROFILE-03 | 草稿应用 / 取消 / 排序 / 移除、服务器默认优先级、原始文本校验、保存冲突、运行与来源快照不随编辑改变 |
| RESOURCE-01 | Pin / Unpin / reorder 策略、写入失败和冲突、原生鼠标拖排与键盘等效操作、重启顺序 |
| RESOURCE-02 | 每类 Sidebar 上限、同名资源身份、已有搜索结果更新、导航对失效 ID 的防护 |

所有真实连接使用隔离 OpenSSH、独立数据目录和一次性凭据。GUI D 重点为紧凑列表、Pinned 拖排与键盘操作、空 Pinned 的间距、连续规则编辑与部分成功的可理解性。

M9 已启用 Profile / Server 删除及影响确认，覆盖 ProfileRunner 中等待凭据和正在启动的任务。Profile 删除保留已接受的启动及现有转发；Server 删除先取消相关工作并停止监听，再原子提交配置。规则移除仍只是可取消的 Profile 草稿操作，既有运行实例保留。详见 [deletion](deletion.md)。自动重连、导入导出、Terminal、完整 Statistics / Settings 和长期通道回收仍按后续里程碑完成；SSH.NET 的诊断与远端监听边界见 [tunnels](tunnels.md)。
