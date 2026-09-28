# Quick Forward 实际 Web 服务访问排查

日期：2026-09-21。状态：已由用户确认是服务器配置填写错误，修改配置后转发正常。用户同时确认 GUI 通过并授权继续 M12。以下检查是收到该确认前的排查过程，不构成产品转发缺陷。

## 反馈与已确认事实

用户反馈远端 npm 应用在 3001 端口提供 MobileGym 页面，命令行 `ssh -N -L 3001:localhost:3001 speechmodel` 可访问，而 Quick 的 Local 显示 EMPTY_RESPONSE，Remote 显示 CONNECTION_REFUSED。

- 接线已在 M7 完成：`DashboardPage.SubmitAsync` → `TunnelWorkspace.SubmitQuickAsync` → `QuickForwardModel.Capture` → `TunnelManager.StartAsync` → `SshNetSession` 中的真实 `ForwardedPortLocal` / `ForwardedPortRemote`。正常配置中也有该用户 Server 的真实运行历史。
- Windows SSH config 的同名别名与应用保存的 Server 在 Host、Port、Username 上均不同；只读登录返回不同 hostname 和网络命名空间。不能据此认定用户原命令在 Windows 执行，更不能擅自改写用户配置。
- 检查时两个 SSH 目标的 3001 均无监听，分别访问 `127.0.0.1`、`localhost`、`::1` 都被拒绝。因此尚不能将当前观测当成原故障发生时的状态。
- 最近一组 3001 Local 历史的 Actual 本地端口仍为 3001；该组记录没有自动递增。两端 Requested host 都是 `127.0.0.1`。
- Local 用于本机访问远端服务；Remote 在 SSH 服务器监听并连接本机目标，不提供本地浏览器入口。
- Quick 基本模式的默认目标是 `127.0.0.1`，与用户命令中的 `localhost` 不同。仅 IPv6 监听是待排除因素，尚未确认是 MobileGym 的实际情况。

## 改动与验证

新增 [QuickForwardHttpTests.cs](../../tests/TunnelDeck.Ssh.Tests/QuickForwardHttpTests.cs)：在隔离 OpenSSH 中分别绑定 IPv4 与 IPv6 loopback，通过 `QuickForwardModel` 的 Advanced `RemoteHost=localhost` 捕获规则并启动 Local 转发，断言 HTTP 响应包含测试页面标题。测试使用一次性凭据，自行清理监听及 HTTP 服务，不连接用户服务器。

补充 [Quick Forward 排查说明](../../docs/quick-forward.md)，说明 SSH 身份、方向、目标地址与 Actual 端口的比较方式。生产默认值、转发实现、用户配置、已保存的规则均未改动。

针对性真实 SSH 验证共 **5 项通过、0 失败、0 跳过**：

- `EngineForwardingTests.HttpWorksThroughBothModesWithoutChangingItsContent`
- `EngineForwardingTests.LocalTargetCanBecomeAvailableWithoutRestartingTheListener`
- `ForwardingTests.ExplicitIpv6LoopbackWorksInBothDirections`
- 新增的 `QuickForwardHttpTests` 两个地址族用例

本地报告：`artifacts/mobilegym-fix/baseline-forwarding.trx`、`artifacts/mobilegym-fix/quick-forward-http.trx`。测试在 Visual Studio 开发环境以 Release / x64 执行。此前 M11 的全量测试与 GUI 证据保持不变；本轮未重新运行全套 GUI。隔离 SSH 夹具已停止，其一次性凭据已删除。

对用户实际服务器的检查仅限本次排障授权下的只读 SSH / HTTP 请求，启用严格主机密钥校验；没有运行配置导入、修改信任、启动或停止用户服务。

## 用户确认

用户回复：「经我测试，是我配置信息填错了，修改后转发正常。GUI 测试通过……可以继续 M12.」因此该问题闭环，不再等待 MobileGym 服务恢复，不修改转发默认地址或网络实现。两项隔离 SSH 回归保留。
