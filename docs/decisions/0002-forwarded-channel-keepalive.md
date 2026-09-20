# ADR 0002：转发通道响应 OpenSSH 存活探测

日期：2026-09-20。状态：已实施并验证。适用 SSH.NET `2026.0.0`，保持该版本和 ADR 0001 的 Local 共享 / Remote 独立传输策略。

## 问题与证据

M7 真实界面流程中，已传输过 payload 的 Remote 转发在空闲约六秒后断开，隔离 sshd 日志为 `Timeout, client not responding`。测试服务器配置 `ClientAliveInterval 2`、`ClientAliveCountMax 2`，客户端默认 KeepAliveInterval 为 20 秒。服务器可以在已有的 forwarded-tcpip 通道上发送 `keepalive@openssh.com` channel request。

固定版本源码显示：

1. [ConnectionInfo.ChannelRequests](https://github.com/sshnet/SSH.NET/blob/2026.0.0/src/Renci.SshNet/ConnectionInfo.cs) 是每个连接的公开可修改字典，默认注册 `keepalive@openssh.com`。
2. [Channel](https://github.com/sshnet/SSH.NET/blob/2026.0.0/src/Renci.SshNet/Channels/Channel.cs) 对已注册请求调用 OnRequest / RequestReceived；[ChannelForwardedTcpip](https://github.com/sshnet/SSH.NET/blob/2026.0.0/src/Renci.SshNet/Channels/ChannelForwardedTcpip.cs) 不处理这类请求，所以没有返回答复。
3. 未注册且 WantReply 的 channel request 则由 Channel 自动发送 `SSH_MSG_CHANNEL_FAILURE`。这个明确的答复足以让 OpenSSH 确认客户端仍然存活。
4. [Session](https://github.com/sshnet/SSH.NET/blob/2026.0.0/src/Renci.SshNet/Session.cs) 已回复未知 global request；本次缺口针对 channel request，不能用全局请求的行为代替分析。

## 决定

`SshSessionFactory` 在创建并连接 SshClient 前，对本次 ConnectionInfo 执行：

```csharp
info.ChannelRequests.Remove("keepalive@openssh.com");
```

让转发通道使用库已有的未知请求答复路径。此改动仅使用公开 API，不修改全局库状态，不依赖反射、私有事件或解析日志；日志仅用于定位受控测试中的问题。维持服务端严格的探测参数和客户端 20 秒 keepalive，不靠缩短客户端周期掩盖缺失答复。认证、host key 校验和物理传输隔离保持原契约。

## 验证与边界

`EngineLifecycleTests.AnIdleForwardedChannelRepliesToOpenSshAliveProbesAndStillTransfersData` 在真实 Remote listener 上建立 TCP 通道，空闲七秒后发送并比对 payload，确认隧道仍为 Running，Stop 后远端监听释放。该用例与全部真实 SSH 测试通过；M7 原生界面流程也验证多条 Remote 在操作、切换布局和手动重试期间继续传输。

这项证据不覆盖任意长期运行或网络黑洞。转发通道的 TCP half-close 传播和长时通道回收仍需在 M10 / M13 独立评估；本次没有修改 OnEof，也不宣称解决这些行为。升级 SSH.NET 时需重新检查公共字典和未知请求答复路径，并保留七秒空闲回归。

同轮夹具的 HTTP 控制服务改为 HTTP/1.1，已有响应均带 Content-Length，以减少经 WSL loopback relay 的连接反复开闭；新用例先执行八路并发数据探测，再验证 100 轮 reserve / listening / release，无重试掩盖。此前偶发的 HTTP 提前结束原因未被证明，不能将其写成已确认的 WSL 缺陷，也不能与 SSH keepalive 修复混为同一问题。
