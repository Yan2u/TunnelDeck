# M3：原子持久化、凭据补偿与启动恢复

日期：2026-09-19。基于 M0–M2 工作区继续实现，保留直接 exe / unpackaged 方式，保留用户确认的 Local-only 占用递增范围。未修改 `.codex/prompt/260919.md` 的用户内容。

## 目的

为后续页面提供可靠、统一的保存入口。失败不能发布未保存的快照，损坏或未来版本不能静默重置配置；配置和 Windows 凭据分开写入时，需要能够补偿并在重启后恢复清理。

## 实现

- `Core/Persistence/ConfigurationService*.cs`：FIFO 提交、expectedRevision、先持久化后发布、普通编辑 / Server 凭据编辑、Flush 与关闭等待；订阅者异常不改变提交结果。
- `Core/Persistence/StateLoadRepair.cs`：仅修复顺序中的重复 / 悬空 / 遗漏 ID 及失效默认选择；保留资源、规则和历史快照。
- `Core/Models`：必要 JSON 字段使用 JsonRequired，避免缺失端口、规则、顺序和计数被默认值掩盖。未知格式的 StateLoadResult 使用 null State + IsReadOnly。
- `Infrastructure/Persistence`：用户数据目录、跨实例事务锁、同目录临时写入 / durable flush / 原子替换、配置备份、严格版本化编解码、显式迁移入口、独立的凭据清理队列。
- `Infrastructure/Security/JsonHostKeyStore.cs`：独立 host + port + algorithm 信任存储；损坏 / 未知格式拒绝使用，禁止自动采用旧备份授予信任。
- `TunnelDeck/Services/AppServices.cs` 与窗口：启动加载、Error / Warning InfoBar、Retry、关闭等待。诊断目录和窗口 probe 同时隔离文件路径与 Credential Manager 命名空间。
- `scripts/Verify-Storage.ps1`：实际启动窗口验证未来版本保护、备份恢复、损坏提示、修复后重试与重启；仅使用独立测试目录。
- README、implementation-plan、UI spec、domain-model、milestones、handoff 同步至 M3 完成 / M4 下一步；详细实现契约新增 `docs/storage.md`。

## 关键边界

新凭据先写持久化清理队列再 CredWrite；提交失败核对磁盘配置再补偿，提交成功才清理旧引用。清理失败返回诊断并保留队列，启动与关闭可重试。原子替换成功后不再通过取消或清理失败把结果改成失败。备份恢复不会恢复旧密码，缺失凭据需在后续认证界面重新输入。

schema 1 是首个实际保存格式。生产不实现臆造的 schema 0；测试通过显式旧格式夹具验证迁移入口、重复加载和提交后的幂等性。未知 schema 不回退旧备份；信任库与清理队列损坏时不自动使用可能过时的备份。

## 验证结果

- `./scripts/Build.ps1 -LockedRestore`：Release x64，0 warnings / 0 errors。
- `./scripts/Test.ps1`：**214 项通过，0 失败，0 跳过**；Core 137、Infrastructure 77（本次新增 75）。覆盖 STORE-01–04 / CRED-01 和主机信任持久化。
- `./scripts/Verify-Storage.ps1`：真实窗口未来版本保护、有备份 / 双损坏提示、Retry、正常加载 / 关闭不改写配置全部通过。
- `./scripts/Verify-Window.ps1`：侧栏固定区域、搜索、pane toggle、短窗、重复关闭与清理期间响应全部通过；本次关闭约 923 ms，包含 750 ms probe。
- 测试临时目录和真实测试凭据已清理；UI 测试保留无秘密的隔离样本与截图。结果摘要位于 `docs/evidence/2026-09-19-m3.json`。
- 本次未改 SSH 传输、未重跑真实 sshd；M2 的 18 项 SSH 结果保留在历史证据中。GUI 人工评测 A 待 M4 正式页面交付后进行。

下一步 M4：Dashboard / Profiles / ProfileDetail / Servers / ServerDetail / Settings 页面、导航 / 搜索与原生布局。M5 再接认证表单、信任确认和连接管理。
