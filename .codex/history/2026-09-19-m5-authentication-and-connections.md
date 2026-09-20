# M5：服务器认证与连接管理

日期：2026-09-19。延续 `codex/m0-m1-foundation` 工作区，不提交或覆盖用户已有改动。用户「OK，继续 M5.」记录为 M4 的 GUI 评测 A 确认；M5 的 GUI 评测 B 仍待用户评估。

## 目的与实现

让服务器配置可以完成真实 SSH 认证，并为 M6 提供有明确身份与释放边界的 session。测试连接使用当前草稿，成功后关闭，不产生 ActiveTunnel 或统计记录。

- `Core/Connections`：SshConnectionManager、ServerUseLease、SshSessionLease、ServerIdentity、HostKeyApproval 与安全错误类型。Local 并发合并；Remote 独立。服务器保存和连接登记共用编辑门禁，覆盖待确认、连接中、运行、待重连及未完成的清理。
- `Infrastructure/Ssh`：SshSessionFactory / SshNetSession / SshForwardHandle。后台读取和解密私钥，使用 SSH.NET ConnectAsync 传递取消；连接超时及迟到结果有明确归属。session 持有校验与错误订阅，清理时退订并释放认证对象；Start / Stop 复用 M1 的控制隔离。
- `Infrastructure/Security`：新增 TrustNewAsync，首连确认不能覆盖其他并发确认保存的不同 key。未知握手先拒绝，批准后重新连接，验证同一个指纹；已知变更或确认后再次变更直接拒绝。
- `TunnelDeck/Services/DialogService`：统一 ContentDialog 队列，包含指纹确认和关闭失败；取消会关闭提示并等待 UI 完成。AppServices / Shell 接入 Connections。
- `ServerDetailPage` / `ServerDetailViewModel`：密码与私钥口令、文件选择器、可选 Remember、缺失凭据提示、Test / Cancel、错误与指纹展示。只 Save 才持久化；清空输入保留已保存凭据，替换 / 忘记沿用 M3 补偿事务；离开页面清空临时输入并取消测试。

## 验证中修正

- 详情资源未出现在 sidebar working set 时，All 入口已处于选中状态。增加 ItemInvoked 处理，使再次点击能回到列表；连接占用通知不再重建侧栏。
- 与现有锁文件一致地显式声明 App RuntimeIdentifiers，解决干净还原中的 NU1004；保留直接 exe、x64 输出路径及所有包版本。
- 原生自动化脚本使用字段 AutomationId 区分 InfoBar 和字段旁的相同错误。兼容 winapp 0.3.1 的属性字符串与 value 查询格式；工具查询失败没有当作应用业务成功。

## 实测

- Release x64、LockedRestore：0 warnings / 0 errors。
- **298 测试通过，0 失败 / 0 跳过**：Core 184、Infrastructure 79、真实 OpenSSH 35。
- 真实 SSH 覆盖密码、两类私钥、错误密码 / 口令、缺失口令、不可读文件、首次拒绝 / 接受及持久信任、两种指纹变更时序、Local 合并 / Remote 隔离、DNS、超时、取消、关闭、断线通知。新 session 适配器还验证 32 KiB payload 与独立 Stop。
- Core 的可控异步假 session 覆盖保存与连接登记竞态、单个等待者取消、关闭期间迟到成功、清理失败重试、无 transport 的重连身份保留及跨服务器独立性。信任库验证并发首连不能覆盖 key。
- `Verify-Connections.ps1` 九组原生流程通过；关闭打开着指纹对话框的窗口约 411 ms。使用隔离配置、临时 SSH 夹具与独立 Windows 凭据前缀，结束时清理一次性凭据与容器。
- `Verify-Shell.ps1`、`Verify-Storage.ps1`、`Verify-Window.ps1` 全部回归通过。750 ms 人为 probe 延迟下关闭约 990 ms，窗口始终响应。
- 实际检查 Light / Dark、900 / 640 epx 服务器表单和指纹对话框；未切换系统 High Contrast。

证据：`docs/evidence/2026-09-19-m5.json`；协议 / 所有权说明：`docs/connections.md`；GUI 结果和截图：`artifacts/m5-review`。计划、设计 spec、README、里程碑和交接说明已同步。

## 后续

GUI 评测 B 由用户确认表单与等待体验。M6 在现有 lease 和控制适配层上完成分配 / 批量调度 / 转发竞态；只有 Local 确认占用允许递增，Remote 仍不递增。M5 未启用 Quick Forward、Profile 规则编辑或 Open Terminal。
