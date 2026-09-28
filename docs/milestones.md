# 里程碑状态

更新：2026-09-23。用户已认可功能及主要 UI / UX；Release x64 本地发布收尾完成，当前报告见 [UI 验证](ui-refresh-validation.md)、[性能](performance.md) 和 [发布](release.md)。M10–M13、UI 重构及收尾代码均保留为未提交工作区改动。

| 里程碑 | 状态 | 交付与边界 |
| --- | --- | --- |
| M0 | 完成 | 保留直接 exe 模板；SDK/NuGet 锁定；x64 构建与实际启动；隔离 sshd 和测试入口；原始模板资源基线 |
| M1 | 风险验证完成，按 ADR 调整传输方案 | 真实 2L+2R、认证/host key、冲突/策略/超时、独立 Remote 隔离；远端不递增现为用户确认的范围 |
| M2 | 完成 | 不可变模型/草稿/快照、Local-only 端口策略、顺序/Sidebar/默认值/重复请求、校验、存储与后端接口 |
| M3 | 完成 | 原子 JSON / 备份 / schema 与 revision、FIFO 提交、持久化凭据补偿队列、独立信任库、启动错误重试与关闭等待 |
| M4 | 完成，GUI 评测 A 已确认 | 用户回复「OK，继续 M5.」；六类页面、导航 / 搜索 / Back、隔离预览、宽窄布局与基础编辑 |
| M5 | 完成，GUI 评测 B 已确认 | 用户「GUI 人工评测通过，继续 M6.」；密码 / 两类私钥、凭据事务、指纹、临时连接测试、身份门禁 |
| M6 | 完成 | 独立于 WinUI 的多规则引擎、真实 Local 端口分配、批量 / 去重 / 限流、请求诊断、迟到工作与退出清理 |
| M7 | 完成，GUI C 已确认 | 用户「GUI 通过，继续 M8.」；真实 Quick Forward / Enter / 高级端点、稳定活动行、Stop / Retry / Remove、调整与 Details、临时凭据、最小 Recent / 启动统计 |
| M8 | 完成，GUI D 已确认 | 用户「GUI 测试通过，继续 M9.」；Profile 规则草稿、跨服务器批量启动、失败项重试、使用计数、Pinned 拖排与 working set |
| M9 | 完成，GUI E 已确认 | 用户「GUI 测试通过，继续 M10。」；单个 / 全量删除影响、待处理工作门禁与取消、原子配置提交、失败重试、引用 / 凭据清理与原生确认 |
| M10 | 完成，GUI F 已确认 | 用户在前一会话回复「GUI 测试通过，继续 M11.」；自动重连、Actual 优先、Stop / Delete / Exit 防复活、有界退出与单实例、100 次循环资源测量 |
| M11 | 完成，GUI G 已确认 | 用户「GUI 测试通过……可以继续 M12.」；严格 JSON 往返、依赖与新 ID 映射、SSH config 预览、原子导入、独立 Open Terminal |
| M12 | 完成，GUI H 已确认 | 用户「GUI 测试通过，继续 M13.」；Recent / Statistics / Settings、宽松 SSH config 导入与默认认证 / 密钥 |
| M13 | 本机验收与本地发布完成 | 功能及主要 UI 已获用户认可；代表性回归、性能测量及 Release x64 ZIP 验证完成。干净机器、多显示器 DPI、200% 系统文字全流程与 Narrator 实际朗读仍保留为验证边界 |

## M12 历史验证

M12 认证默认值反馈：**663 项通过、0 跳过**（Core 428、Infrastructure 148、真实 SSH 87），Release x64 锁定构建 0 warnings / 0 errors。标准 `IdentityFile` 原本已支持；本轮新增 Settings 默认认证与密钥配置、id_rsa 检测、新建 Server 预填、无 IdentityFile 的 SSH config 导入默认值，以及内部 schema 1 → 2 兼容迁移。已有服务器、草稿与 JSON 显式认证不被覆盖。

`Verify-AuthenticationDefaults.ps1` 的八组原生检查全部通过：旧配置加载不写盘、密码模式隐藏密钥字段、自动检测、文件选择 / 取消、新 Server 预填、导入优先级、无效路径 / 磁盘失败 / Retry、500 epx 深色布局及重启。`Verify-Storage.ps1` 回归通过，包括未来 schema 3 只读保护、备份恢复与外部修复重试。真实 SSH 用默认与显式两种来源的密钥认证，并验证转发 payload。

完整 Transfer 回归**未通过**：两次均已通过文件保存选择器、导出排除秘密、预览取消、非法 JSON 和原子失败重试，在 500×580 缩窗后滚动 SSH 预览时退出。Windows 事件记录与既有 `E_SURFACE_CONTENTS_LOST` / Microsoft.UI.Xaml 渲染问题一致；保留两份失败结果，未通过反复运行挑选绿色结果，也未压制异常。这一已知问题仍列为 M13 的优先跟进项，不能用认证功能测试通过代替整套 GUI 验收。

本轮证据见 [认证默认值 evidence](evidence/2026-09-21-m12-authentication-defaults.json)，改动记录见 [认证默认值历史](../.codex/history/2026-09-21-m12-authentication-defaults.md)。普通用户配置和本地 SSH config 未改动；新评测入口为 `scripts/debug/Show-AuthenticationReview.ps1`。

## M12 初版验证（历史）

M12 自动化结果：**641 项通过、0 跳过**（Core 414、Infrastructure 142、真实 SSH 85），Release x64 锁定构建 0 warnings / 0 errors。来源、时间、DST、迟到事件裁剪、累计计数、设置边界、并发保存和失败重试已验证。新的 SSH config 规则对本机文件只读预览得到 9 条可导入、0 条跳过；实际提交用隔离样例验证，未改写用户 SSH config 或普通应用配置。

**五套原生检查通过**：Activity / Settings / 改进导入，Transfer，Shell，真实 Forwards，以及导入预览 20 轮 500×580 / 1200×760 缩放。新的活动区、保存失败重试、首次展开、窄窗重排、忽略 SSH 选项、全不可用提示、重启和真实转发闭环均有证据。

初次 SSH 回归的一项断线恢复测试读到了旧句柄清理前的过渡态；修正为等待恢复组登记，完整 85 项 SSH 重跑通过，生产重连代码未修改。原生验证中曾出现异常退出，WER 的一次记录指向 Microsoft.UI.Xaml / `E_SURFACE_CONTENTS_LOST`。完整流程重跑及最终正式构建的 20 轮缩放通过；未稳定复现和确认根因，**保留为 M13 跟进项，不声称已修复**。

证据见 [M12 evidence](evidence/2026-09-21-m12.json)，契约见 [活动与设置](activity-and-settings.md) 和 [导入导出](transfer.md)，历史见 [M12 记录](../.codex/history/2026-09-21-m12-activity-settings-and-ssh-import.md)。原里程碑证据保留。

## M11 验证（历史）

M11 自动化结果：**606 / 606 通过，0 跳过**（Core 387、Infrastructure 136、真实 SSH 83），相对 M10 增加 94 项。Release x64 锁定构建 0 warnings / 0 errors。

覆盖 Profile 跨服务器导出依赖与顺序、新 ID / 引用映射、重名不合并、严格 schema / 重复字段与 ID / 端口与引用校验、路径缺失、取消和磁盘故障的原子性、预览后删除 / Delete all 重新确认 / 退出拒绝、SSH config 与 Windows OpenSSH 首次取值对照、真实 argv 接收与 Windows Terminal、真实 SSH 独立生命周期。导入后 Local / Remote 都可传输；重连期间导入与随后删除不会复活原服务器。

**六套原生检查通过**：M11 Transfer 的 11 组检查，以及 Shell、Resources、Connections、Forwards、Lifecycle 回归。覆盖系统文件选择器及取消、预览只读、非法 JSON、文件锁下失败与重试、引用映射、SSH config 警告与明确跳过、500 epx 窄窗固定按钮与滚动、Light / Dark、终端缺失密钥与未保存草稿、重启后保留资源且不自动连接。

证据见 [M11 evidence](evidence/2026-09-21-m11.json)，契约见 [transfer](transfer.md) 和 [terminal](terminal.md)，历史见 [M11 改动记录](../.codex/history/2026-09-21-m11-transfer-and-terminal.md)。本轮回归使用 OutputDirectory 将证据保存在 artifacts/m11-review，保留原里程碑截图。

~~~powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Test.ps1 -Ssh -NoFixtureBuild
./scripts/debug/Verify-Transfer.ps1
~~~

## M10 历史验证

M10 自动化结果：**512 / 512 通过，0 跳过**（Core 342、Infrastructure 92、真实 SSH 78）。Release x64 锁定还原构建 0 warnings / 0 errors。相对 M9 增加 38 项 Core 与 15 项真实 SSH 用例。

重连验证覆盖假时钟下的退避 / 抖动 / 窗口、共享 Local 与独立 Remote 断线后恢复、另一台服务器持续传输、上次 Actual 端口优先与确认冲突递增、Remote 拒绝不移动端口、认证 / host key / 信任库故障不再弹窗、预算耗尽、退避期间 Stop / Delete / Exit、未确认 Remote 申请在三种动作下保持所有权、sshd 重启只恢复曾建立的转发、网络黑洞下本地先释放而远端等待 sshd 检测。100 次真实 Local + Remote 启停循环后会话全部释放、端口可复用，预热后句柄 +5、线程 0、工作集约 +8 MB（单次隔离测量，见 `artifacts/m10-review/cycles.json`）。

`Verify-Lifecycle.ps1` 的 11 组检查通过：同目录别名在持有配置锁时转交激活且不写配置、最小化窗口还原、隔离目录并行实例、正常关闭、真实 Profile 转发、断线倒计时 / Details「Previous listener」/ Stop 可用 / 等待期间不写配置、恢复后同一行同一端口与计数不变、退避期间 Stop 只取消该行、文件锁下关闭出现 Retry / Exit without saving 与 Esc 暂停、重试后保存停止记录、显式不保存退出保持旧 JSON、连续三次关闭合并为一次有界退出。完整契约见 [lifecycle](lifecycle.md)，证据见 [M10 evidence](evidence/2026-09-20-m10.json)。

~~~powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Test.ps1 -Ssh -NoFixtureBuild
./scripts/debug/Verify-Lifecycle.ps1 -NoFixtureBuild
~~~

真实测试使用隔离数据目录、一次性凭据和 OpenSSH 8.9p1，未连接用户实际服务器。已确认普通 sshd 监听重启保留已建立会话，夹具新增「终止客户端」故障；Remote 传输失败瞬间的清理竞态已修复。托盘未实现，关闭窗口仍是退出；TCP half-close 与长期通道回收仍不在已验证范围。

## M9 历史验证

M9 自动化结果：**459 / 459 通过，0 跳过**（Core 304、Infrastructure 92、真实 SSH 63）。Release x64 锁定还原构建 0 warnings / 0 errors。相对 M8 增加 21 项 Core、5 项 Infrastructure 和 3 项真实 SSH 用例。

删除验证覆盖重名与去重计数、1000 条规则、确认期间影响改变、等待凭据 / 连接测试 / 迟到监听的取消、另一服务器继续运行、第二个 Stop 失败、真实磁盘故障、共享凭据、配置发布后的异常、排队保存与关闭。Profile 删除保留真实 Local / Remote payload 和来源快照，Server 删除确认释放本地 / 远端监听。

`Verify-Deletions.ps1` 的 14 组检查通过：单个和全量确认、0 项隐藏、按 Profile 分组、长清单滚动和固定按钮、Light / Dark、900 / 640 / 500 epx、Enter / Esc 默认取消、详情草稿、导航 / 搜索清理、4 条 Profile / Quick 实际转发、文件锁下停止但保留配置、重新确认并重试、空 Profile、全部端口释放、历史保存和重启。额外回归及文件散列见 [M9 evidence](evidence/2026-09-20-m9.json)，完整契约见 [deletion](deletion.md)。

本轮还修复了窗口销毁后才 Hide 删除对话框的崩溃，以及保存失败后成功重试仍残留已停止活动行的问题。对话框在活窗口上先取消并排空；终态行在停止记录持久化成功后才清理。

**九套原生界面检查全部通过**：Deletions、Profiles、Forwards、Connections、Resources、Shell、Window、Dashboard、Storage。Quick 按键驱动增加前台与输入焦点确认，连接表单导航使用 UIA Invoke；资源拖排保留前台中断保护，被其他窗口打断后安全终止并重新验证成功。

~~~powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Test.ps1 -Ssh -NoFixtureBuild
./scripts/debug/Verify-Deletions.ps1 -NoFixtureBuild
~~~

真实测试使用隔离数据目录、一次性凭据和 OpenSSH 8.9p1，未连接或删除用户实际服务器。M9 交付时重连 / 导入尚未实现；DELETE-07 的对应交错已在后续 M10 / M11 验证。

## M8 历史验证

M8 自动化结果：**430 / 430 通过，0 跳过**（Core 283、Infrastructure 87、真实 SSH 60）。Release x64 锁定还原构建 0 warnings / 0 errors。相对 M7 增加 23 项 Core 与 3 项真实 SSH 用例；原引擎、输入、凭据、信任、持久化和 keepalive 回归全部通过。

完整证据见 [M8 evidence](evidence/2026-09-20-m8.json)，契约见 [profiles](profiles.md)、[quick-forward](quick-forward.md)、[tunnels](tunnels.md)、[connections](connections.md)、[shell-and-navigation](shell-and-navigation.md) 和 [storage](storage.md)。

~~~powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Test.ps1 -Ssh -NoFixtureBuild
./scripts/debug/Verify-Profiles.ps1 -NoFixtureBuild
./scripts/debug/Verify-Resources.ps1
./scripts/debug/Verify-Forwards.ps1 -NoFixtureBuild
./scripts/debug/Verify-Dashboard.ps1
./scripts/debug/Verify-Shell.ps1
./scripts/debug/Verify-Window.ps1
./scripts/debug/Verify-Connections.ps1 -NoFixtureBuild
./scripts/debug/Verify-Storage.ps1
~~~

夹具未改动，复用 M7 已构建的 OpenSSH 8.9p1 镜像；修改夹具后应去掉 NoFixtureBuild。所有 SSH 验证均使用隔离服务器、数据目录和一次性凭据，不读取用户 SSH 配置或连接用户实际服务器。

M8 真实 SSH 验证跨服务器 Local / Remote payload 与重复 Start all、独立停止、另一台服务器拒绝后只重试失败规则；代理持有真实远端确认时取消，远端监听释放、已成功 Local 继续传输。Core 覆盖内容冲突、取消草稿、默认服务器、失败提交重试、启动去重、临时凭据范围、迟到句柄与另一 Profile 的独立等待者。

**八套原生界面检查通过**。Profile 流程覆盖完整编辑 / Save / Cancel、按 Server 合并凭据、指纹、部分成功 / Retry failed、纯重复、修改配置后原隧道继续传输、单条停止、关闭 / 重启。资源流程验证 Profiles / Servers 的实际鼠标拖排、键盘菜单移动、跨列表拒绝、文件锁下回退与 Retry、空 Pinned、实时资源改名 / 搜索、每类 Sidebar 上限和持久顺序。Quick Forward、24 行 Dashboard、Shell、窗口、连接表单和存储恢复回归通过。

本次单次测量：真实 Quick 混合转发退出约 248 ms；带 750 ms probe 的窗口重复关闭约 1076 ms，保持响应；首个窗口句柄约 749 ms。这些是各自隔离场景的测量，不作为通用性能保证。测试驱动保留前台、鼠标按键和坐标检查，原生拖放过程中不反复激活窗口；外部读配置对替换期间的短暂缺失 / sharing violation 做有界等待，写入失败仍由应用明确报告。

Local 目标拒绝仍未完整透传到 SSH.NET 公共异常事件，Running 只表示监听建立；Remote 失败仍不能区分占用与服务端策略，也不自动递增。长期通道回收和 TCP half-close 不在 M7 keepalive 修复的已验证范围内，见 [ADR 0002](decisions/0002-forwarded-channel-keepalive.md)。

## GUI 人工评测

**GUI A–G 已由用户确认，M12 GUI H 待评测**。本轮用户确认「GUI 测试通过……可以继续 M12.」，见 [M11 GUI 记录](../.codex/history/2026-09-21-m11-gui-accepted.md)。用户也已确认 MobileGym 的转发问题是配置填写错误，修正后正常。旧证据中的 Pending 为生成时占位，不能撤销人工确认。Agent 未切换系统 High Contrast。

普通模式读取保存配置，启动本身不连接服务器；M12 评测窗口使用隔离数据与样例活动，不自动连接任何服务器。

可用 scripts/debug/Show-ActivityReview.ps1 重新打开隔离评测窗口；它创建独立数据目录、200 条样例活动、同名 / 改名 / 删除来源以及统计数据。

认证反馈使用 scripts/debug/Show-AuthenticationReview.ps1，直接展示 Settings → Server authentication。Private key 仅在隔离评测数据中预选；普通应用仍保留用户已有设置。

GUI H 重点（用户只评测交互，解析、计数和故障由 Agent 验证）：

- Recent 的来源、方向、结果与本地时间可读，展开历史不会抢占核心操作。
- Statistics 中累计总量、当前资源排名及使用次数的含义清楚。
- Sidebar 3–10、默认 Server 与自动找端口默认值易用；修改后保存，当前 Quick 草稿保留。
- 默认认证切换时密钥字段显示 / 隐藏，自动检测与 Browse 选择易用，新建 Server 与 SSH config 导入按默认值预填。
- 窄窗辅助区域上下排列；SSH config 忽略选项提示清楚，必填字段有效时确认按钮可用。

可复现样例见 Verify-Activity.ps1 / Verify-Transfer.ps1；不需要用户连接真实服务器或重复协议测试。

## 下一步

M12 / GUI H 已确认，继续 M13 的综合验收与发布。导入页异常已从原始转储定位到获取焦点期间重置列表，当前修正与验证见 [M13 记录](m13-validation.md)。首版整体尚待最终验收。
