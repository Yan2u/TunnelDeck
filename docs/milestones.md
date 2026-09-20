# 里程碑状态

更新：2026-09-20。当前分支 `codex/m0-m1-foundation`，代码基于 `2039b14f9f83616748b2471f343defd1d58710f7` 的工作区改动。

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
| M9 | 实现与自动化完成，GUI E 待评测 | 单个 / 全量删除影响、待处理工作门禁与取消、原子配置提交、失败重试、引用 / 凭据清理与原生确认 |
| M10–M13 | 未开始 | 重连 / 运行稳定性 / 导入导出 / 完整统计 / 正式验收与发布 |

## 本轮验证

M9 自动化结果：**459 / 459 通过，0 跳过**（Core 304、Infrastructure 92、真实 SSH 63）。Release x64 锁定还原构建 0 warnings / 0 errors。相对 M8 增加 21 项 Core、5 项 Infrastructure 和 3 项真实 SSH 用例。

删除验证覆盖重名与去重计数、1000 条规则、确认期间影响改变、等待凭据 / 连接测试 / 迟到监听的取消、另一服务器继续运行、第二个 Stop 失败、真实磁盘故障、共享凭据、配置发布后的异常、排队保存与关闭。Profile 删除保留真实 Local / Remote payload 和来源快照，Server 删除确认释放本地 / 远端监听。

`Verify-Deletions.ps1` 的 14 组检查通过：单个和全量确认、0 项隐藏、按 Profile 分组、长清单滚动和固定按钮、Light / Dark、900 / 640 / 500 epx、Enter / Esc 默认取消、详情草稿、导航 / 搜索清理、4 条 Profile / Quick 实际转发、文件锁下停止但保留配置、重新确认并重试、空 Profile、全部端口释放、历史保存和重启。额外回归及文件散列见 [M9 evidence](evidence/2026-09-20-m9.json)，完整契约见 [deletion](deletion.md)。

本轮还修复了窗口销毁后才 Hide 删除对话框的崩溃，以及保存失败后成功重试仍残留已停止活动行的问题。对话框在活窗口上先取消并排空；终态行在停止记录持久化成功后才清理。

**九套原生界面检查全部通过**：Deletions、Profiles、Forwards、Connections、Resources、Shell、Window、Dashboard、Storage。Quick 按键驱动增加前台与输入焦点确认，连接表单导航使用 UIA Invoke；资源拖排保留前台中断保护，被其他窗口打断后安全终止并重新验证成功。

~~~powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Test.ps1 -Ssh -NoFixtureBuild
./scripts/Verify-Deletions.ps1 -NoFixtureBuild
~~~

真实测试使用隔离数据目录、一次性凭据和 OpenSSH 8.9p1，未连接或删除用户实际服务器。重连 / 导入尚未实现，DELETE-07 中这两类完整交错分别留给 M10 / M11；已有门禁与排队配置校验是后续接入点。

## M8 历史验证

M8 自动化结果：**430 / 430 通过，0 跳过**（Core 283、Infrastructure 87、真实 SSH 60）。Release x64 锁定还原构建 0 warnings / 0 errors。相对 M7 增加 23 项 Core 与 3 项真实 SSH 用例；原引擎、输入、凭据、信任、持久化和 keepalive 回归全部通过。

完整证据见 [M8 evidence](evidence/2026-09-20-m8.json)，契约见 [profiles](profiles.md)、[quick-forward](quick-forward.md)、[tunnels](tunnels.md)、[connections](connections.md)、[shell-and-navigation](shell-and-navigation.md) 和 [storage](storage.md)。

~~~powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Test.ps1 -Ssh -NoFixtureBuild
./scripts/Verify-Profiles.ps1 -NoFixtureBuild
./scripts/Verify-Resources.ps1
./scripts/Verify-Forwards.ps1 -NoFixtureBuild
./scripts/Verify-Dashboard.ps1
./scripts/Verify-Shell.ps1
./scripts/Verify-Window.ps1
./scripts/Verify-Connections.ps1 -NoFixtureBuild
./scripts/Verify-Storage.ps1
~~~

夹具未改动，复用 M7 已构建的 OpenSSH 8.9p1 镜像；修改夹具后应去掉 NoFixtureBuild。所有 SSH 验证均使用隔离服务器、数据目录和一次性凭据，不读取用户 SSH 配置或连接用户实际服务器。

M8 真实 SSH 验证跨服务器 Local / Remote payload 与重复 Start all、独立停止、另一台服务器拒绝后只重试失败规则；代理持有真实远端确认时取消，远端监听释放、已成功 Local 继续传输。Core 覆盖内容冲突、取消草稿、默认服务器、失败提交重试、启动去重、临时凭据范围、迟到句柄与另一 Profile 的独立等待者。

**八套原生界面检查通过**。Profile 流程覆盖完整编辑 / Save / Cancel、按 Server 合并凭据、指纹、部分成功 / Retry failed、纯重复、修改配置后原隧道继续传输、单条停止、关闭 / 重启。资源流程验证 Profiles / Servers 的实际鼠标拖排、键盘菜单移动、跨列表拒绝、文件锁下回退与 Retry、空 Pinned、实时资源改名 / 搜索、每类 Sidebar 上限和持久顺序。Quick Forward、24 行 Dashboard、Shell、窗口、连接表单和存储恢复回归通过。

本次单次测量：真实 Quick 混合转发退出约 248 ms；带 750 ms probe 的窗口重复关闭约 1076 ms，保持响应；首个窗口句柄约 749 ms。这些是各自隔离场景的测量，不作为通用性能保证。测试驱动保留前台、鼠标按键和坐标检查，原生拖放过程中不反复激活窗口；外部读配置对替换期间的短暂缺失 / sharing violation 做有界等待，写入失败仍由应用明确报告。

Local 目标拒绝仍未完整透传到 SSH.NET 公共异常事件，Running 只表示监听建立；Remote 失败仍不能区分占用与服务端策略，也不自动递增。长期通道回收和 TCP half-close 不在 M7 keepalive 修复的已验证范围内，见 [ADR 0002](decisions/0002-forwarded-channel-keepalive.md)。

## GUI 人工评测

**GUI A / B / C / D 已由用户确认，M9 GUI E 待评测**。最新用户确认是「GUI 测试通过，继续 M9.」。旧脚本生成的 Pending 是自动化占位，不能撤销人工确认；Agent 未切换系统 High Contrast。

~~~powershell
./scripts/Run.ps1
~~~

普通模式读取保存配置，启动本身不连接服务器；最终评测窗口使用隔离数据，启动身份和截图保存在 M9 证据。布局预览也可使用 Run.ps1 -Preview Sample 或 Stress，预览禁止网络。

GUI E 重点：

- Servers / Profiles 的 Delete 与 Delete all 位于 More，危险操作与 Add 的层级清楚。
- 单 Server 的分组规则和全量去重摘要易读，0 项隐藏，Profile 删除说明保留服务器和现有转发。
- 长清单与窄窗能滚动，Cancel / Delete 始终可用；默认 Enter 和 Esc 取消。
- 失败提示能解释已停止但尚未删除的状态，Review and retry 可理解；文件 / 网络故障已由自动化验证，无需人工重做。

## 下一步

根据 GUI E 反馈完善后进入 M10：自动重连、最后 Actual 端口恢复、Stop / Delete / Exit 期间防复活、单实例和完整关闭恢复。复用 ServerWorkRegistry、ProfileRunner、TunnelWorkspace 与 TunnelManager，不绕过删除门禁与版本校验。Terminal / 导入导出留给 M11，完整设置与统计留给 M12。
