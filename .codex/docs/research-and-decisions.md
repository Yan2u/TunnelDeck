# TunnelDeck：技术核实与规划决策

调研日期：2026-09-19。本文为实施计划的依据，不代表已在用户的空白模板或真实服务器上完成验证。公开文档、库的 develop 分支与最终安装的 NuGet 版本可能不同；M0 锁定版本，M1 对该版本验证。

## 1. 对前期建议的修正

### 1.1 `-R` 不能可靠区分“占用”和“策略拒绝”

[RFC 4254 §4](https://www.rfc-editor.org/rfc/rfc4254.html#section-4) 定义的 `SSH_MSG_REQUEST_FAILURE` 没有错误原因字段。§7.1 的 `tcpip-forward` 使用此全局请求机制。不能把 §5.1 **通道打开**失败的 reason code 套用到**远端监听申请**失败。

[SSH.NET ForwardedPortRemote 源码](https://github.com/sshnet/SSH.NET/blob/develop/src/Renci.SshNet/ForwardedPortRemote.cs) 在收到失败后抛出通用 `SshException`。因此：

- `-L`：真实绑定后，可依据 `SocketError.AddressAlreadyInUse` 递增；AccessDenied 可能来自 Windows 保留端口/策略，不应直接宣称“被占用”。
- `-R`：连接正常并收到明确监听拒绝时，可以在用户开启自动找端口后做有限顺序尝试，但不知道拒绝是否由占用引起。
- 认证失败、host key 不匹配、DNS 错误、连接中断、超时、取消，不进入端口搜索。
- 不通过解析本地化异常文案进行稳定错误分类；对缺乏公开结构化结果的库行为，M1 必须验证适配器能否可靠识别，不能把所有 `SshException` 都当监听拒绝。
- 若固定版本不能可靠区分监听拒绝与其他异常，默认降级为原因不明即停止；将有限重试作为明确尚未完成的能力，不伪称满足自动递增。
- 不以远程执行 `ss/netstat` 为默认诊断方式：需要 shell 权限、受平台影响，且先检查再绑定仍有竞争条件。
- 不使用 `-R 0` 代替顺序递增：服务器分配端口不等于从 N+1 开始逐个尝试。

成功文案应对应证据：本地明确占用可以写“Port 3000 was already in use”；远端仅拒绝应写“The server rejected the requested listening port. Port 8001 was assigned.”。

### 1.2 一个 SSH 连接可以承载多条规则，但控制操作需串行

[SshClient API](https://sshnet.github.io/SSH.NET/api/Renci.SshNet.SshClient.html) 提供 `AddForwardedPort` / `RemoveForwardedPort`；[ForwardedPort API](https://sshnet.github.io/SSH.NET/api/Renci.SshNet.ForwardedPort.html) 提供 `Start` / `Stop` / `IsStarted` / `Dispose`。

RFC 的全局请求回复不带请求类型或独立请求 ID，要求按请求顺序回复。SSH.NET 当前源码在远端转发对象上订阅 session 回复。应用层应按 ServerId 串行执行连接、转发 start/stop 等控制操作，并在 M1 实测共享连接上的多条 `-R`；不同服务器可有限并发，数据传输仍并行。

不要把一个远端超时当作“无副作用的失败”后立即申请下一条。超时后服务端可能已经创建监听；必要时使该 session 失效并整体重建，避免延迟回复被新操作误认。停止超时也不能只凭本地 `IsStarted == false` 推断远端监听已经消失。

### 1.3 监听成功不等于目标服务可达

`-L` 先创建本地 listener，目标连接通常在访问该 listener 时才建立；`-R` 同样可能接受监听请求，但转发目标未运行。Active 表示转发已建立，不应宣传为应用服务健康检查。验收必须传输真实 payload，不能只看 `IsStarted`。

### 1.4 身份校验需显式实现

[HostKeyEventArgs API](https://sshnet.github.io/SSH.NET/api/Renci.SshNet.Common.HostKeyEventArgs.html) 提供 `CanTrust`、公钥和 SHA256 指纹；[源码](https://github.com/sshnet/SSH.NET/blob/develop/src/Renci.SshNet/Common/HostKeyEventArgs.cs) 的默认信任行为不能代替应用自己的信任策略。

首连未知 host key：拒绝本次握手，取得待确认指纹，在 UI 上确认并保存后重连；重连仍校验同一 host key。这样无需在 SSH 事件中同步阻塞 UI 等待。已知 key 改变时拒绝连接，由用户在明确的信任管理操作中处理。仅显示 fingerprint 而不验证无效。

密码、私钥口令通过 [Windows Credential Manager / CredWriteW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew) 保存，JSON 只保存凭据引用。默认不记住，明确选择记住后才写入。测试使用独立命名空间，不能操作用户真实凭据。

### 1.5 首版统计不要假设能直接取得流量

本次查阅的 `ForwardedPort` 公共 API 有请求和异常事件，没有直接的每隧道双向字节累计接口。`RequestReceived` 不是字节数。首版建议实现成功启动次数、最近活动、常用服务器和 Profile；隐藏尚未实现的流量指标。

若必须精确计量，应先定义业务 payload 与 SSH 传输字节的口径，再为代理/流计量评估额外复制、监听和维护成本；不能用网卡流量或进程总流量冒充隧道流量。

### 1.6 退出清理的保证有边界

正常退出：取消新操作和重连 → 停止规则 → 关闭 SSH session → 保存配置 → 退出。WinUI 的 [AppWindowClosingEventArgs](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindowclosingeventargs?view=windows-app-sdk-1.8) 支持取消关闭；应先取消本次关闭，异步清理完成后再关闭，并防止重入。

应用崩溃时本地进程/socket 可由操作系统回收，但网络黑洞或断电时，远端 `-R` 监听释放取决于 sshd 检测到连接断开。客户端不能承诺任何网络故障下都“瞬时释放远端端口”。自动化测试分别覆盖正常断开和无响应网络；后者需记录服务端存活检测条件。

删除服务器时，配置变更可以统一提交；已经停掉的运行时隧道无法事务回滚。部分停止成功、随后停止失败时：保留所有配置、不删除服务器，如实刷新已停止隧道，不擅自自动重启来伪装回滚。

## 2. WinUI 选型依据

- [TitleBar 控件](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/title-bar) 原生支持 pane toggle、搜索控件、标题和 caption 按钮留白。实现 `TitleBar + NavigationView + Frame`，不另画窗口按钮。
- [NavigationView](https://learn.microsoft.com/en-us/windows/apps/design/controls/navigationview) 提供 header/footer、自由 pane 内容及 Auto/Compact/Minimal 行为。固定 Dashboard、底部 Settings/状态、中间独立滚动，需要在目标 SDK 的实际模板上验证；单纯把所有项目加到 MenuItems 中并不能保证固定布局。
- [WinUI Gallery](https://github.com/microsoft/WinUI-Gallery) 的 ListView 拖排示例包含 `CanDragItems`、`CanReorderItems`、`AllowDrop`。Pinned/Other 分开，仅接受同一 Pinned 集合内部的拖放。
- Attention InfoBadge 是状态显示控件。使用原生、可聚焦的轻量 Button 承载 InfoBadge 和 Flyout，提供 Automation 名称；不要只绑定 pointer 事件。
- 双 ListView 与统一页面滚动存在布局/虚拟化权衡。必须明确滚动所有者，并做大数据验收，不照搬“外层 ScrollViewer 套无限高 ListView”。
- [打包说明](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-packaging-model)：MSIX/unpackaged 与框架依赖/self-contained 是两个独立维度。继承空白模板的现有模式，先只维护一条开发运行路径；实际测量运行库体积与应用体积。

## 3. 首版边界与可调整的规划默认

下表是尚未由本次澄清答复确认时的**规划默认**，不是用户已同意的事实。后续答复优先更新此表及主计划。

| 项目 | 规划默认 | 改变时影响 |
| --- | --- | --- |
| 关闭窗口 | × 停止全部并退出；最小化保持运行；首版不强制引入托盘 | 若要求托盘，M10 增加显式驻留设置及托盘验收 |
| SSH 范围 | SSH.NET 直连，密码/私钥，有限 SSH config 导入 | ProxyJump、agent、FIDO2 会改变 M1/M5/M11 的范围 |
| 远端递增 | 连接正常且可确认监听拒绝时有限顺序重试，文案保留原因不确定性 | 严格模式则 `-R` 原因不明不递增，须注明能力缺口 |
| 流量统计 | 后续版本，首版隐藏 | 强制首版计量需增加独立技术验证和实现里程碑 |
| 自动搜索 | 默认开启，最多 20 个候选端口（含原端口），不越过 65535；同时受总时限约束 | 常量/设置，不更改 Requested 端口 |
| 地址范围 | TCP；默认双方 loopback，高级字段可显式设 bind/target；首版不做 UDP/SOCKS | 额外协议应另立范围 |
| 批量启动 | 逐条报告；成功条保留，失败条可重试；不承诺全有或全无 | 全量回滚增加事务和重连复杂度 |
| 重复启动 | 完全相同 requested endpoint/mode 的启动幂等，导航到现有条目 | 不将双击自动变成另一条递增端口规则 |
| Profile 生命周期 | 配置模板；运行中隧道持有快照，删除/编辑 Profile 不自动影响其已启动隧道 | 删除 Server 仍必须停止相关隧道 |
| Server 编辑 | 名称可随时改；连接身份/凭据变更在有活动/待重连隧道时要求先停止 | 防止同一 ServerId 下同时存在不一致连接 |
| 编辑保存 | Profile/Server 详情使用显式 Save/Cancel；Settings、Pin/排序即时保存 | 未保存离开时仅在将丢失修改时确认 |
| Quick Server | 普通选择跨页面保留；单独的 Quick 固定选择跨重启保留；与资源列表 Pin 无关 | 删除 Server 清理三种引用 |
| Recent | 成功启动、成功连接测试、成功启动终端时更新 LastUsedAt；浏览页面不算使用 | 不改变 All 页 Other 顺序 |
| 历史/计数 | 本地保留最近 200 个完成的转发事件；累计成功启动及使用次数独立持久化 | 最近日志裁剪不重置累计统计 |
| 语言 | 首版英文 UI 与给定文案一致；字符串资源化，中文实施文档 | 中文 UI 可另加资源字典，无需改模型 |
| 发布 | Windows 11 x64 为首要验收环境，保留模板打包模式；完成 M13 后再考虑 ARM64 等 | 不在计划阶段声称兼容所有 Windows/CPU |

模板所在路径、SDK/NuGet 确切版本及打包方式未在当前工作区确定。当前工作区为 Python tools，故这里只交付规划文件；M0 必须在实际 TunnelDeck 模板项目中核对。

## 4. OpenSSH 和导入的边界

[Win32-OpenSSH 项目范围](https://github.com/PowerShell/Win32-OpenSSH/wiki/Project-Scope) 仍将 Client ControlMaster 列为不支持；该页面较旧，应在需要 OpenSSH 后端时以所选二进制实测，而不是推定 Unix 控制 socket 可直接复用。首版的 Open Terminal 使用外部终端独立连接，不复用 SSH.NET session。

[OpenSSH 配置语义](https://github.com/openssh/openssh-portable/blob/master/ssh_config.5) 规定一般选项采用第一个取得的值，另有 Host 模式、Include、Match、ProxyJump 等。有限导入必须展示支持范围和逐条警告，不静默忽略关键选项后把错误配置当成成功导入。

不在导入时盲目执行 `ssh -G`：任意配置可能涉及 `Match exec` 等行为。首版用纯文本白名单解析；不支持的复合配置要求用户在预览页补齐，或者跳过该条。导出 TunnelDeck JSON 不携带密码、私钥内容或“已信任”主机状态，不覆盖用户原始 SSH config。

## 5. 技能采用说明

已读取并参考：

- `C:\Users\19559\.agents\skills\exa-relay-research\SKILL.md`：官方来源搜索与正文核实。
- `C:\Users\19559\.codex\skills\winui-app\SKILL.md`：项目、窗口、生命周期和验收结构。
- `C:\Users\19559\.claude\plugins\cache\win-dev-skills\winui\0.3.0\skills\winui-design\SKILL.md`：使用随附目录检索 ListView、InfoBadge 和 NavigationView 示例。
- 同目录技能集合中的 `winui-setup\SKILL.md`：仅参考 M0 环境检查项，本次任务未安装或升级工具。
- `C:\Users\19559\.agents\skills\ui-ux-pro-max\SKILL.md`：在 `tools` conda 环境中使用 `--stack winui` 查询。

通用 design-system 查询给出了面向 Web 的不适用建议（视频首屏、单一深色模式、自定义配色），未采纳。原始 77 条 WinUI 规范和官方 WinUI 行为优先于通用设计建议。GUI 人工评测按用户要求安排，不因技能建议就强制引入 WinAppDriver/Appium。
