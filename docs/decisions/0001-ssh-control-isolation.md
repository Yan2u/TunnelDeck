# ADR 0001：SSH 控制隔离与 M1 边界

日期：2026-09-19。状态：实施中的技术决策；后续用户已确认仅保留本地端口占用自动递增。依据 SSH.NET `2026.0.0` 的固定源码和真实 OpenSSH 8.9p1 夹具。以下传输策略更新原计划“一台 Server 始终只用一个物理 session”的假设。

## 实测事实

1. 同一 SSH 连接上的两条 Local 和两条 Remote 能传输 32 KiB 二进制 payload。停止一条之后其余条仍可传输；正常路径可复用连接。
2. 本机真实绑定冲突得到 `SocketException` / `AddressAlreadyInUse` / 10048。AccessDenied 不应当作“端口占用”。
3. 远端端口占用、`AllowTcpForwarding no`、`PermitListen` 拒绝均得到相同的普通 `Renci.SshNet.Common.SshException` 和 HResult。公开 API 没有稳定的拒绝原因字段。
4. 多个 `ForwardedPortRemote` 都订阅 session 的全局 success/failure 事件。第二条 Remote 的成功回复会同时置位第一条的 AutoResetEvent。第一条 Stop 可能消耗旧信号，在本次取消确认被 TCP 代理扣住时仍于约 1 ms 返回。
5. 单纯串行 Start/Stop 不足以消除此旧信号。上游 Stop 的 WaitAny 超时结果也没有向调用者报告为失败。
6. 目标端口关闭时，Local listener 仍运行，消费侧 TCP 流结束。本次未观察到 `ForwardedPort.Exception` 事件，因此不能承诺该事件报告每次目标拒绝，更不能以 Running 代表目标服务健康。

`UpstreamBehaviorTests` 是固定版本行为刻画。它通过表示问题成功复现，不表示共享 Remote 的实现安全。真正的适配层保障由 `ControlSessionTests` 和 `TimeoutTests` 单独证明。

## 决定

- 每个 ServerId 仍由一个逻辑协调器管理配置、身份和删除门禁。
- **Local 规则共享一条 Local-only SSH 传输；每个 Remote 规则独占一条 SSH 传输。** `SshControlSession` 拒绝在 Remote 传输上增加另一条规则，也拒绝混合 Local/Remote。
- 每条物理传输内仍用异步门禁串行控制。应用操作预算必须小于 SSH.NET `ConnectionInfo.Timeout`；另用单调时间检查防止计时器调度延迟把迟到结果当成功。
- 取消、超时、连接失败和不明确的 Remote 错误永久 retire 旧传输，观察清理任务后再释放门禁。后续操作必须创建新传输；迟到回复不跨传输复用。
- `-R` 失败后停止，保留原因不明的错误；**远端自动递增已按用户要求移出当前范围**。不依赖异常文本，不将一般 `SshException` 当作确定的端口占用。
- M2/M6 的 `-L` 搜索仅对可确认的 AddressAlreadyInUse、最多 20 次、同时有截止时间。`-R` 始终只申请一次，即使配置错误地携带自动搜索标志也不递增。

这条路径仅使用公开 API。代价是 N 条同时运行的 Remote 规则需要 N 条 SSH 连接；两条 Local 加两条 Remote 使用 3 条连接。后续 M6/M13 必须计入这个开销。密码/口令缓存和 host key 检查仍由 Server 级资源管理，不能因为独立传输放松身份校验。

## 适配层验证

- 独立 Remote Stop 在当前 sshd 回复未释放时保持等待；释放后完成；重复 Stop 无副作用。
- 一条 Remote Stop 超时只 retire 它自身传输，其余两条 Local、一条 Remote 继续传输 payload。
- 已创建真实远端监听但确认被扣住：Start 超时/取消后销毁旧传输，监听消失；新传输可以重新建立并传输。
- 同一 Remote 传输上第二条规则在发出网络请求前被拒绝。
- 普通远端拒绝 retire 传输，不重试另一个端口。
- 未知和同 endpoint 已变更 host key 均在 `IsAuthenticated` 变为 true 前拒绝；密码、加密 Ed25519 私钥及 IPv4/IPv6 loopback 均已验证。

## 继续沿用的规划默认

以下仍是原始规划默认，不表示用户另行明确确认：× 停止并退出、SSH.NET 直连、密码/私钥、有限 SSH config 导入、首版隐藏累计流量。批量部分成功、Requested/Actual 分离、Profile 运行快照、重复请求幂等和统计口径继续按原计划。

关闭正常连接可以验证本地及远端监听释放；断电/网络黑洞下远端释放取决于 sshd 存活检测。本轮不宣称任何异常网络下都瞬时释放。

## 依据

- [固定源码 ForwardedPortRemote](https://github.com/sshnet/SSH.NET/blob/7b2fd3dbf2c86a80a7b06cea020aa5f821c9902e/src/Renci.SshNet/ForwardedPortRemote.cs)
- [固定源码 ForwardedPortLocal](https://github.com/sshnet/SSH.NET/blob/7b2fd3dbf2c86a80a7b06cea020aa5f821c9902e/src/Renci.SshNet/ForwardedPortLocal.cs)
- [RFC 4254 §4 / §7.1](https://www.rfc-editor.org/rfc/rfc4254.html)
- [Microsoft Learn TitleBar](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/title-bar)
- 仓库 `tests/TunnelDeck.Ssh.Tests`、TRX 及 `docs/evidence/2026-09-19-m0-m1.json`。
