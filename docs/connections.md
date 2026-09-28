# M5 认证与连接契约

更新：2026-09-20。实现包括服务器凭据表单、原生指纹确认、临时连接测试和 session 生命周期。M6 已接入[多规则引擎](tunnels.md)，M7 已接入 [Quick Forward](quick-forward.md)，M8 已接入 [Profile 批量启动](profiles.md)；自动重连留待 M10。

## 连接与身份所有权

`Core/Connections/SshConnectionManager` 按 ServerId 协调工作。`ServerUseLease` 保留服务器身份，应覆盖规则的 Starting、Running 和 Reconnecting 全过程；它可以在没有物理连接时继续存在。`SshSessionLease` 只代表一份连接引用，调用者释放 lease，不能直接 Dispose 共享的 `Session`。

```csharp
// 单次使用：最后一个 Local 引用释放后关闭物理连接。
await using SshSessionLease lease = await connections.AcquireAsync(serverId, ForwardMode.Local, credentials, cancellationToken);

// 需要跨重连保留身份时，外层 reservation 不随某一次失败连接释放。
using ServerUseLease use = await connections.ReserveAsync(serverId, cancellationToken);
await using SshSessionLease attempt = await connections.AcquireAsync(use, ForwardMode.Local, credentials, cancellationToken);
```

- 同 Server 的 Local 并发请求共享一次连接建立流程和后续传输；Remote 每次 acquisition 独立，不能混入 Local pool 或跨规则共享，见 [ADR 0001](decisions/0001-ssh-control-isolation.md)。
- 待连接、等待用户决定、有效 session 和未完成的清理都计入占用。最后一个引用释放后立即清理，不保留无使用者的空闲连接。
- `Connections.SaveServerAsync` 与连接登记使用同一个服务器门禁，门禁覆盖配置提交完成。占用期间只允许不改变连接身份的编辑；DisplayName / LastUsedAt 不构成连接身份，凭据引用及 Remember / Forget 的实际变更构成身份变更。
- 修改服务器应经过这个入口。M3 的 `ConfigurationService` 仍负责存储事务；未来导入和删除必须在连接层完成相同的占用检查，不能直接绕过门禁改写服务器。
- 显示名变化不会重建连接。不同服务器的网络操作独立；用户指纹确认不持有服务器编辑门禁或 session 控制锁。

## 认证与指纹

`Infrastructure/Ssh/SshSessionFactory` 使用 SSH.NET 2026.0.0，支持密码、未加密私钥和有口令私钥。密钥读取、解密和库的同步认证部分在后台执行。错误被映射为固定文案和 `ConnectionFailure`，不向 UI 携带 SSH.NET 原始异常、密码或密钥内容。

新物理连接先用拒绝信任的握手取得协商后的主机公钥算法；该握手不进入认证。管理器随后读取独立的 `host-keys.json`：

1. 已知且相同的指纹可以继续；已知但不同的指纹直接拒绝。
2. 未知指纹通过 `IHostKeyApproval` 请求用户确认。取消时不写入信任。
3. 接受后使用 `TrustNewAsync` 原子保存；若其他并发操作已保存不同的 key，不能覆盖它。
4. 新建认证连接，`HostKeyVerifier` 只接受刚确认的完整 host / port / algorithm / fingerprint 身份。确认后 key 再变仍然拒绝。

因此一次连接建立流程通常包含两次握手。Local 并发合并针对完整流程；不能把探测握手统计为另一条活动连接。`TrustAsync` 保留为显式管理存储的底层接口；连接流程使用禁止替换的 `TrustNewAsync`。损坏或未知版本信任文件继续失败关闭，不自动回退旧信任。

M6 的 `HostKeyApprovalCoordinator` 按规范化 host / SSH port / algorithm / fingerprint 合并并发首连的确认与保存；认证连接仍各自精确校验，Remote 传输不共享。共享操作内再次读取 trust，避免并发完成后的重复对话框。单个等待者取消不影响其他等待者；最后一个取消会关闭并排空对话框，取消后迟到接受不能授信。不同指纹不合并，且不能覆盖先保存的信任。

`DialogService` 通过 DispatcherQueue 显示原生 ContentDialog，并串行管理 host key 与关闭失败对话框。取消会 Hide 正在显示的对话框，并等 ShowAsync 真正结束后释放队列位置。服务器 endpoint 使用同一 IPv6 格式化规则；指纹可选中复制，默认按钮为 Cancel。

## 凭据与表单

只有 Save 会调用 M3 的凭据事务。Remember 写入 Windows Credential Manager 的新引用；成功提交后清理旧引用，失败走现有持久补偿日志。密码、私钥口令和文件内容不进入配置 JSON。表单只保存私钥路径，支持原生文件选择器。

已保存秘密不读回 PasswordBox。空输入加已选 Remember 表示保留已有凭据；输入表示替换，取消 Remember 并 Save 表示忘记。连接身份改变后不隐式把旧凭据用于新目标。找不到保存的凭据时，字段旁提示重新输入；失败内容同样只使用安全文案。离开页面会清空临时输入。

Test Connection 对当前未保存草稿建立独立临时连接，即使同服务器已有运行连接，也需要实际验证该次凭据。成功返回前关闭临时 session；不保存草稿、不创建 ActiveTunnel、不修改 LastUsedAt / Recent / Statistics。预览模式在 UI 和连接边界都禁止网络访问。

M7 Quick Forward 在密码未保存且需要新连接时先收集临时输入；取消不创建失败实例。重复请求或已有共享 Local 传输可以直接复用；保存的凭据缺失、认证失败和私钥口令错误仍通过安全失败结果进入输入 / Retry 流程。原生对话框使用既有排队与取消机制，关闭时清空 PasswordBox，不写 Credential Manager、不跨新连接缓存秘密。永久保存仍必须通过 Server 的 Remember / Save。

M8 Profile 的一次启动操作内按 Server 合并临时输入，可服务该批次的多条独立 Remote 连接；结束后清空引用，不跨操作缓存。各服务器的规则独立完成，取消一个输入不会回滚其他服务器已成功的转发；host key 仍按每条物理连接验证。

ServerDetail 展示字段错误、进度、取消入口、成功或失败状态和已验证指纹。测试期间可操作窗口和导航；离开页面取消该次测试，迟到结果不会更新已离开的页面。工作占用事件仅刷新表单可用性，不重建侧栏。All 入口处理 ItemInvoked，确保未出现在 working set 的详情页可以通过再次点击已选中的 All 返回列表。

## 超时、取消与退出

默认连接预算 10 秒、SSH.NET 内部超时 15 秒、控制操作预算 5 秒，keepalive 间隔 20 秒。连接完成还会检查单调时间预算。取消由库的 ConnectAsync token 传入；始终观察实际任务结束，迟到成功会被释放，不能取消包装 Task 后丢下 client。

SSH.NET 2026.0.0 注册了 `keepalive@openssh.com` channel request，但转发通道不处理其事件。M7 在创建 client 前从公开的每连接 `ChannelRequests` 字典移除该项，使库对 WantReply 请求发送已有的 CHANNEL_FAILURE 答复，OpenSSH 据此确认存活。严格服务端探测下，Remote 通道空闲七秒后仍可传输的回归已通过；依据与未覆盖边界见 [ADR 0002](decisions/0002-forwarded-channel-keepalive.md)。这不改变默认 keepalive 周期和信任策略。

一个 Local 等待者取消不会取消其他等待者。最后一个等待者取消后会等待实际清理；UI 仍然异步可用。清理失败继续占用身份并保留任务，关闭时可重试。shutdown 阻止新连接和编辑，取消排队确认和连接，等待所有 session / 未完成工作，再排空配置提交。关闭窗口的等待超时不会停止后台清理或把它误报为完成。

M8 的应用关闭顺序为 Profiles → Forwarding → Tunnels → Connections → Activity → Configuration；先排空批量与 Quick 命令，再释放网络，最后保存离散历史和使用次数。正常退出会记录已运行实例的停止结果。

`SshNetSession` 保持 host-key 与 ErrorOccurred 订阅，断线时通知 lease 使用者，释放时退订并释放认证 / 私钥对象。M6 的 `SshSessionLease.InvalidateAsync` 失效整个物理连接；Dispose 失败后的重试不重复扣减借用者。`SshControlSession` 始终追踪实际 worker，取消或超时后先关闭 client 再等待工作结束。Start / Stop 的候选、事件和迟到结果由适配器与 TunnelManager 联合持有，详见 [tunnels](tunnels.md)。

## 验证入口与人工评测

```powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Test.ps1 -Ssh -NoFixtureBuild
./scripts/debug/Verify-Connections.ps1 -NoFixtureBuild
./scripts/debug/Verify-Forwards.ps1 -NoFixtureBuild
./scripts/debug/Verify-Shell.ps1
./scripts/debug/Verify-Window.ps1
./scripts/debug/Verify-Storage.ps1
```

首次运行或修改夹具后省略 NoFixtureBuild。M5 夹具增加未加密 Ed25519 私钥，与原密码、加密私钥、可更换 host key、策略限制和受控 TCP 代理一起覆盖 AUTH-02 / AUTH-03 / CONN-01–03。纯 Core 测试使用可控制完成顺序的假 session 验证保存竞态、独立服务器、单个等待者取消、关闭与迟到成功、清理失败重试。

GUI 脚本创建独立数据目录与 SSH 容器，凭据命名空间按目录散列隔离。脚本抑制 PasswordBox 设置命令的响应，最后清理临时凭据和容器；截图使用目标窗口渲染。M5 原始证据见 `evidence/2026-09-19-m5.json`，当前回归见 `evidence/2026-09-20-m8.json`。

GUI B 已由用户「GUI 人工评测通过，继续 M6.」确认，GUI A 已由「OK，继续 M5.」确认，GUI C 已由「GUI 通过，继续 M8.」确认，GUI D 已由「GUI 测试通过，继续 M9.」确认。M9 继续回归连接表单、指纹、取消、关闭，并覆盖删除等待连接测试的情形；删除契约见 [deletion](deletion.md)，GUI E 待评测。Agent 未切换系统 High Contrast，不把 Light / Dark 或整体 GUI 确认描述为该项实测。

控件参考：[WinUI Gallery PasswordBox](https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Samples/PasswordBox/PasswordBoxPage.xaml)、[WinUI Gallery ContentDialog](https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Samples/ContentDialog/ContentDialogPage.xaml)，以及本机 WinUI 2.3.9 的 metadata / generic.xaml。
