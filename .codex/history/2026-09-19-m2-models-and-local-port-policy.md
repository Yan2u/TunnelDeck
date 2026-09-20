# M2：不可变领域模型与仅本地端口递增

用户明确要求目前取消远端端口失败自动递增，保留 Local 本地监听端口确认占用时的递增。本次同步修改 implementation-plan、ui-ux-spec、research-and-decisions、ADR 0001、README 与交接信息；Remote 编辑器隐藏自动搜索选项、保存 false、不显示自动调整 badge，通用错误指向手动恢复。它已移出当前范围，不再列为首版缺口。

Core 新增 Models / Drafts / Snapshots / Runtime / Validation / Policies / Abstractions。配置使用不可变 record 和不可变集合；草稿保留原始输入并在 Build 时校验；Profile 启动快照复制服务器、规则和名称。运行资源、主机信任和密码从 AppState 契约中排除。

实现 PortSearchPolicy（Local-only、20 次、65535、单调时钟预算及取消）、ResourceOrderService（Pin/Unpin/reorder）、SidebarProjection（各自上限、Pinned + Recent）、RuleDefaultsResolver（逐级跳过已删除 Server）、DuplicateTunnelPolicy（Requested 路由与生命周期判断）、TunnelStatePolicy（停止/代次约束）。不把纯策略当成 M6 实际网络 allocator 或并发引擎。

增加结构化字段校验和 AppState 完整性检查，定义后续存储/SSH/终端接口；WindowsCredentialStore 实现核心凭据接口。主机地址规范化由 trust 与去重共用，并补齐 IDN 和 IPv6 格式边界。

验证：Core 137 / Infrastructure 2 / SSH 18，共 157 项通过、0 跳过。包括所有错误类型下 Remote 不递增、边界端口、取消/超时、稳定排序、默认服务器、跨 Server 快照、身份不依赖名称、停止中的重复请求、JSON round-trip、未知 schema 和缺失必需字段。原有真实 OpenSSH 认证、host key、转发、取消/超时隔离回归通过，夹具及临时凭据已清理。

详细契约见 `docs/domain-model.md`，证据见 `docs/evidence/2026-09-19-m2.json`。保持 unpackaged x64 exe 构建模型；UI 本轮不扩展，人工 GUI 状态仍待评测。后续从 M3 原子持久化继续。
