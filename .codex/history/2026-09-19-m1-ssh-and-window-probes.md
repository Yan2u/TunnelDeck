# M1：真实 SSH 和窗口能力验证

目的：在实现业务页面前确认协议、安全、清理和原生窗口能力。

新增主机密钥 identity / verifier、Windows Credential Manager 封装、`SshControlSession` 控制边界、可重入请求合并的 `ShutdownCoordinator` 与最小 AppServices 注入入口。

真实测试覆盖密码和加密 Ed25519 私钥、未知/变化的 host key、2L+2R 的二进制 payload、单条及重复停止、IPv4/IPv6、本地占用、远端占用/禁止/PermitListen、目标不可达、加密 TCP 回复延迟及取消/超时之后的释放。

发现 SSH.NET 2026.0.0 的 Remote 全局回复会广播给全部远端对象；串行化仍可能让 Stop 使用旧回复。本轮采用公共 API 的适配方案：Local-only 传输共享，Remote 一条一传输；控制截止时间小于库内部 timeout，超时/取消/未知 Remote 结果后 retire 旧传输。新增回归测试证明一条 Remote Stop 超时不影响其他三条转发。远端自动递增仍未完成，原因不明时停止，见 `docs/decisions/0001-ssh-control-isolation.md`。

窗口以 Mica + 原生 TitleBar + NavigationView + Frame 构成。Dashboard 在 PaneHeader；两个资源区共用原生 MenuItemsScrollViewer；Settings/状态在 FooterMenuItems。FoundationPage 明确显示开发阶段；`--window-probe` 显示样本并加入 750 ms 清理延迟，不读写用户配置。标题栏搜索、pane toggle、主题切换和异步关闭有实际窗口证据。

验证入口：`scripts/Test.ps1 -Ssh`、`scripts/Verify-Window.ps1`。最终 26 / 26 自动化测试通过，0 跳过（Core 6、Infrastructure 2、真实 SSH 18）。GUI 自动验证约 770 ms 出现窗口，重复关闭约 902 ms 正常退出；窗口布局和输入检查通过。人工 GUI 状态保留为待评测；M4 的正式页面与视觉评测未开始。

用户的四项产品选项仍沿用规划默认，不写成用户已明确确认。后续从 M2 继续，必须先读当前 milestones 与 ADR，避免恢复已被实测否定的物理 session 共享假设。
