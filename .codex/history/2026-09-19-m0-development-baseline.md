# M0：开发和验证基线

目的：在已有魔改 WinUI 3 模板上建立可重复的开发路径，为后续层级提供真实验证条件。

保留 `TunnelDeck/TunnelDeck.csproj` 名称、.NET 10 Windows 目标、Windows App SDK 2.5.1 和 unpackaged 直接 exe 模型。增加 global.json、NuGet lock files、.editorconfig、统一 x64 解决方案映射和按平台隔离的 NuGet 中间目录。没有安装/升级 Visual Studio、Windows SDK 或 winapp。

新增 `src/TunnelDeck.Core`、`src/TunnelDeck.Infrastructure`，以及独立 Core / Infrastructure / SSH xUnit 项目。`scripts/Build.ps1`、`Run.ps1`、`Test.ps1` 复用规定的开发环境；测试数据只进入仓库忽略目录。

`test-infra/sshd` 使用固定 Ubuntu 摘要及 OpenSSH 8.9p1-3ubuntu0.17，包含四个 sshd、echo 服务及受 token 保护的控制服务。容器只向 loopback 发布端口；随机口令、加密私钥和 host key 每次生成。Start 拒绝非空输出目录，建立受控 WSL 驻留；Stop 按标签/run ID 清理本次容器及凭据。

验证：原始 Release 模板 0 warning / 0 error，真实响应窗口标题 MainWindow；60 秒空闲样本 Private Bytes 约 91.3 MiB、Working Set 123.5 MiB、归一 CPU 0.00217%。原始冷启动耗时未单独采样。后续构建/测试和窗口证据见 `docs/baseline.md`、`docs/evidence/2026-09-19-m0-m1.json`。

实施中修复了 WSL 命令结束后容器随发行版退出、旧 manifest 误作 readiness、HTTP chunked/body 长度约定以及不同平台还原目录冲突的问题。最终 SSH 夹具按建立→测试→清理的同一脚本生命周期执行。

限制：本机工具链就绪不等于正式发布完成；Windows 11 x64 为本次实际环境。完整产品数据与业务模型属于 M2/M3，未提前标记完成。
