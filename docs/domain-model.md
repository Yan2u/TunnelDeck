# M2 领域模型与策略

更新：2026-09-20。实现位于 `src/TunnelDeck.Core`，不引用 WinUI、SSH.NET 或数据库框架。这里定义可独立测试的配置、快照和决策；原子写盘已在 M3 接入，M6 已完成[真实监听分配与转发引擎](tunnels.md)，M7 增加[界面命令和活动记录](quick-forward.md)。

## 配置、草稿与快照

| 区域 | 类型 / 用途 |
| --- | --- |
| Models | RemoteServer、ForwardProfile、ForwardRule、AppSettings、ResourceOrder、ActivityEntry、UsageStatistics、AppState |
| Drafts | ServerDraft、ProfileDraft、ForwardRuleDraft；保留原始端口文本，Build 时一次性校验 |
| Snapshots | ProfileLaunchSnapshot、RuleLaunchSnapshot、ForwardSourceSnapshot；复制规则、服务器及显示名称 |
| Runtime | ActiveTunnel、TunnelState；记录 Requested / Actual、来源、UTC 时间、代次和停止标志 |
| Security | HostKeyIdentity / TrustedHostKey；主机信任独立存储 |
| Validation | 配置、AppState、端口、host 校验，返回带字段路径的诊断 |

持久化对象是不可变 record，集合使用 ImmutableArray / ImmutableDictionary。草稿是独立可编辑对象；更改或丢弃草稿不改变已保存配置，保存候选也不会跟着后续草稿编辑变化。Profile 可以跨服务器；显示名允许重复，Server 显示名也允许为空，所有身份判断按 Guid。

`ProfileSnapshotFactory.Capture` 验证引用后复制每条规则、对应 Server 和来源名称，按原规则顺序生成启动快照。后续编辑、改名、删除不会修改快照。历史中的已删除资源 ID 是合法的来源信息；只有当前配置的引用要求仍存在。

`ActiveTunnel.IsRunning` 仅对应 Running。`TunnelStatePolicy` 提供合法状态转换、停止标志和代次校验。M6 的 TunnelManager 持有句柄、会话及未完成的清理任务，通过实例身份与停止状态防止迟到结果复活。ActiveTunnel 新增实际调整原因、申请次数 / 搜索结束原因和请求诊断快照；句柄、网络对象与运行统计不进入 AppState。

M7 增加 `ActiveTunnel.EndedAt`，仅在实际清理完成后记录，用于终态 duration 和离散历史。`TunnelActivityRecorder` 按运行 ID / 生命周期阶段去重，Started / Stopped / Failed / Cancelled 写入最近 200 个事件；每个新成功实例增加 TotalForwards / ServerForwards 并更新 Server.LastUsedAt，重复提交不计数，写盘失败保留可重试队列。Profile 批次使用和重连统计仍待后续里程碑；schema 仍为 1。

## 仅本地监听占用自动递增

用户已明确确认目前不支持 Remote 失败自动递增。`PortSearchPolicy` 的规则是：

1. 首次申请 Requested 端口。
2. 只有 Local + AutoFindAvailablePort + LocalAddressInUse 才产生下一个候选。
3. 只改变 LocalPort；目标端和 Requested 保持不变。
4. 最多 20 次（含原端口），到 65535 停止；总预算默认 10 秒、每次默认最多 2 秒，并把末次预算限制为剩余时间。
5. 使用 TimeProvider 的单调时间，取消、超时、AccessDenied、认证、host key 和网络错误都不能继续递增。
6. Remote 始终只有一次申请；即使输入带了错误的自动搜索标志，候选也强制关闭该标志。

`PortSearch` 是每个请求单独持有的顺序游标，不执行网络操作。M6 的 TunnelManager 已对真实 Start 应用每次的 timeout / CancellationToken；适配器释放失败候选，控制边界失效并排空不确定的传输。纯策略和真实端口分配分别由 Core 与受控 SSH 测试验证。

保存的 Remote 规则必须有 `AutoFindAvailablePort=false`。编辑草稿进入 Remote 模式时有效值为 false，回到 Local 可恢复该草稿原先的选择。Settings 默认值只作用于新建 Local 草稿，不回写旧规则。

端口输入只接受 1–65535 的整数字符串或有限整数 NumberBox 数值，拒绝小数、NaN、无穷、指数/分组形式与旧值回退。监听 bind 必须使用显式 IP；目标可以使用 IP 或 hostname，解析由网络层按正确端点视角完成。默认双方 `127.0.0.1`；Remote 的真实流向为 Remote → Local。

## 资源顺序、Sidebar、默认值

- Add / Unpin 插入 Other[0]；Pin 追加至 Pinned 尾部。重复 Pin、Unpin 或 Add 不意外重排。
- 只有 Pinned 可以重排，且必须保留完全相同的一组 ID；键盘移动使用 MovePinned。
- 顺序操作返回候选 ResourceOrder，不修改输入。M3 提交成功后调用者才发布到 UI。
- Sidebar 两类资源分别受 3–10 的上限控制，先按 Pinned 顺序取值，再按 LastUsedAt 降序补 Recent 并去重。没有 LastUsedAt 的未使用条目不作为 Recent；相同时间按 Guid 升序确定稳定顺序。
- 最近使用只改变投影，不改变 All 页的 Other 数组。
- 新 Rule 的默认 Server：上一条规则 → Quick 当前选择 → Quick 固定选择 → Settings 默认 → 未选择；每一层都跳过已删除和空 ID。Quick 当前/固定合计对应规范中的第二级。

## 重复请求

`ForwardRequestKey` 使用 Mode + ServerId + 两端 Requested host/port。host 做 IP / IDN / 大小写规范化，不查询 DNS，因此 localhost 与 127.0.0.1 不擅自合并。RuleId、ProfileId、自动搜索标志和 Actual 端口不参与去重。

`DuplicateTunnelPolicy` 对 Starting / Running / Reconnecting 返回复用，对正在停止或有停止请求的工作返回 WaitForStop，对终态返回可创建。首次来源快照不被另一 Profile 的重复请求覆盖。M6 已在同一门禁下原子检查并登记 Starting；清理失败继续保留请求键，迟到句柄未释放前不能创建重复实例。

## 持久化契约与接口

AppState schema 为 1。使用 System.Text.Json 的 camelCase 命名；根字段以及端口、规则集合、顺序、设置、计数等必要字段标记 JsonRequired，避免数据缺失被当作默认值。读取未知 schema、非法枚举、重复 ID、缺失引用、异常顺序、非法端口、非 UTC 时间或负计数都会得到明确诊断。AppStateValidator 本身不修复、不重置、不写文件；M3 的 StateLoadRepair 仅修复顺序中的 ID 和失效的默认 Server 选择。

Root 仅保存资源、四个顺序数组、Settings、Recent 和累计 Statistics。ActiveTunnel、IForwardHandle、SshClient、Socket、密码、私钥内容和主机信任不在 Root 中。CredentialReference 只保存 Guid；ConnectionCredentials 是临时数据，Secret 标记 JsonIgnore，ToString 隐去值。

`Abstractions` 定义 IStateStore、ICredentialStore、IHostKeyStore、ITerminalLauncher、ISshSessionFactory / ISshSession / IForwardHandle；M3 新增 IConfigurationLock 和 ICredentialCleanupStore。JsonStateStore、WindowsCredentialStore、JsonHostKeyStore 已实现对应接口。IStateStore 检查 expectedRevision，原子写盘后返回实际提交的 AppState；未知版本的 StateLoadResult 为 `State=null, IsReadOnly=true`，不能以空快照替代。

## 验证与后续

M2 的 DOM / ORDER / DEFAULT 用例由 Core 的 137 项测试覆盖；加上 Infrastructure 2 项及真实 SSH 18 项回归，共 157 项通过。测试时钟和固定种子的顺序操作序列不使用真实 sleep。报告见 `docs/evidence/2026-09-19-m2.json`。

M3 已完成原子 JSON、备份 / 迁移入口、凭据补偿及用户数据目录，详见 [storage](storage.md)。M4 已接入正式页面、导航 / 搜索、基本元数据保存，GUI 评测 A 已确认；纯导航和展示查询位于 Core 的 Navigation / Presentation，详见 [shell-and-navigation](shell-and-navigation.md)。M5 新增 Connections，包括 ServerUseLease、SshSessionLease、SshConnectionManager、服务器身份门禁和安全错误边界，详见 [connections](connections.md)。

M6 新增 Tunnels，包括批量结果、按服务器调度、真实 Local-only 端口分配、取消 / Stop / Remove / Shutdown 所有权、请求错误聚合及 `ForwardActivity.Revision` 乱序防护；契约和 TUN-01–08 证据见 [tunnels](tunnels.md)。

M7 在 Presentation 新增 QuickForwardModel、TunnelItem、TunnelWorkspace 和 IViewDispatcher；输入、稳定行、凭据恢复、后台通知与关闭均可脱离 WinUI 测试。30 项新增 Core 用例覆盖 VM-01–04、历史提交失败重试和预览命令门禁，Core 合计 260 项；完整验证见 [quick-forward](quick-forward.md)。

M8 新增 ProfileEditorModel / RuleEditorModel / ProfileRuleItem、ProfileRunner 和不可变批量结果。ProfileEditPolicy 对可编辑内容做冲突检测，ConfigurationService 的 Profile 保存保留新的 LastUsedAt，重排只核对对应 Pinned 顺序。ProfileUses 按至少一次新成功实例的启动操作计数；访问和纯重复不计数。新增 23 项 Core 用例，当前 Core 283 项，详见 [profiles](profiles.md)。
