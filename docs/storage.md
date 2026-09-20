# M3 存储与凭据契约

更新：2026-09-19。入口为 `ConfigurationService`，磁盘适配位于 `TunnelDeck.Infrastructure/Persistence`。保留 unpackaged / 直接 exe 模型。

## 数据位置

正常运行使用 `%LOCALAPPDATA%\TunnelDeck`，不写安装目录。

| 数据 | 位置 / 行为 |
| --- | --- |
| 配置 | `state.json`，包含资源、四个顺序数组、Settings、RecentActivity 和累计 Statistics |
| 配置备份 | `state.json.bak`，正常替换时保留上一份配置；首次创建没有上一版备份 |
| 主机信任 | `host-keys.json`，与配置完全分离；导入或编辑 Server 不授予信任 |
| 凭据清理队列 | `credential-cleanup.json`，只记录待核对的 Guid，不含秘密 |
| 密码 / 私钥口令 | Windows Credential Manager，`TunnelDeck/Credentials/<guid>` |
| 事务锁 | `configuration.lock` 及各 JSON 的 `.lock` 文件；排他文件句柄保护事务，进程退出自动释放 |

私钥仍只保存路径。AppState 不保存 ActiveTunnel、连接句柄、密码、私钥内容或 host key 信任。恢复配置不会自动恢复运行中的隧道。

`--window-probe` 自动选择 `%TEMP%\TunnelDeck\WindowProbes\<guid>`。`Run.ps1 -DataDirectory <absolute path>` 可显式使用诊断目录；两者均使用 `TunnelDeck/Isolated/<directory hash>` 凭据前缀，不访问正常运行的凭据命名空间。文件测试使用独立的 `%TEMP%\TunnelDeck.Tests\<guid>`，结束时校验路径再删除；真实 Credential Manager 测试使用随机 `TunnelDeck/Tests/<guid>`。

## 提交顺序和取消

`ConfigurationService.UpdateAsync`、`SaveServerAsync` 进入同一个 FIFO 队列。每个候选基于前一笔成功提交的不可变快照生成；Pin、排序、默认值和普通配置编辑没有旁路。显式编辑草稿应传入打开草稿时的 `expectedRevision`，冲突后保留草稿、重新加载并让用户处理，不能静默覆盖。

一次修改持有配置事务锁，凭据补偿也在此锁内完成。`JsonStateStore` 另持有配置文件锁，在锁内重新读取磁盘 revision，与 expectedRevision 比较，再递增一次。不同协作实例不会同时写同一文件；其他编辑器直接改写文件不受这些锁约束。

写入步骤：

1. 校验完整候选和引用，序列化到 UTF-8 JSON。
2. 在同目录以唯一名字创建临时文件，写入后执行异步 flush 与 `FileStream.Flush(true)`，关闭句柄。
3. 在发布前最后检查取消；首次创建用 `File.Move`，已有文件用 `File.Replace`。
4. 原子发布成功即为提交成功，返回递增后的 revision，再发布到内存。之后的取消、临时文件清理或 UI 订阅者异常不能把成功报告成失败。

临时文件写入、flush、替换前失败时不发布候选，队列仍可重试。文件适配器在替换后报告 I/O 异常时，在锁内比较目标内容以确认是否已经提交。未发布的临时文件尽力删除；崩溃遗留的 `.tmp` 不会被当作配置加载。

正常编辑立即写盘。`FlushAsync` 等待此前接收的队列项；`ShutdownAsync` 停止接收新修改并等待已接收项。关闭等待被取消不会反向取消正在完成的保存。当前没有 Recent debounce；未来批量活动更新也必须经过同一队列。

M7 的 TunnelActivityRecorder 通过此 FIFO 保存离散启动 / 结束事件及累计启动次数；M8 增加按操作去重的 ProfileUses / LastUsedAt。请求计数与 duration 不写盘，失败事件留在内存队列供重试；正常关闭在 Configuration.Shutdown 之前刷新 Activity。Profile 保存保留新的使用时间并只检查可编辑内容冲突，见 [profiles](profiles.md)；强制终止的边界见 [quick-forward](quick-forward.md)。

## 加载、修复与迁移

schema 1 是首个实际持久化格式，使用大小写严格的 camelCase 字段和数字枚举。必需字段、重复 JSON 属性、未知字段、未知枚举、端口、ID、规则引用、时间和计数均校验。缺少规则数组或计数不能变成空集合 / 0。

| 情况 | 结果 |
| --- | --- |
| 主文件和备份均不存在 | 新的空配置、revision 0；加载本身不创建 `state.json` |
| 主文件有效 | 返回已校验的配置 |
| 主文件丢失或损坏，备份有效 | 返回备份和恢复诊断；加载不改写原文件，下一次成功编辑保留有效备份 |
| 主文件 / 备份均损坏 | 明确错误，禁止保存空配置替代 |
| 主文件为未来或未支持的 schema | `State=null, IsReadOnly=true`；不尝试用旧备份降级覆盖 |
| 只能找到未支持版本的备份 | 同样只读，不建立空配置覆盖它 |

自动修复仅包含：在已确认资源 ID 唯一的前提下，去掉顺序数组的重复 / 悬空 ID，按资源原数组顺序把遗漏 ID 追加到 Other；清空已失效的 DefaultServerId / PinnedQuickServerId。每项修复都有字段路径诊断。重复资源 / Rule ID、失效的 Rule.ServerId、非法端口等不会通过删除资源来“修好”。历史快照和统计中的已删除资源 ID 保留。

修复和迁移先在内存形成候选，加载不写盘，下一次成功编辑才保存标准格式。`IAppStateMigration` 提供显式、单调前进的迁移入口；当前没有已发布的旧 schema，因此生产不臆造 schema 0 语义。测试注册确定性的旧格式夹具迁移，验证重复加载、提交后重载均不会重复转换或额外递增 revision。

UI 的 `StorageNotice` 对恢复 / 修复显示 Warning，对不能加载 / 未支持版本显示 Error，并提供数据目录与 Retry。正常加载不弹成功提示。配置服务自身阻止只读或未初始化状态的编辑，后续页面也应据此禁用保存。

## 凭据事务与清理

`CredentialChange` 只有 Keep、Forget、Remember 三种意图，秘密属性不序列化，ToString 隐去值。UI 不传入自行生成的凭据引用；`SaveServerAsync` 负责分配新 Guid，普通 `UpdateAsync` 禁止引入新的凭据关联。

M5 的 UI 经 `SshConnectionManager.SaveServerAsync` 进入这里：先用与连接登记共享的门禁保护身份编辑，再完成本节的持久化事务。活动、待重连或尚未清理的工作禁止替换身份 / 凭据；只改显示名称仍可提交。详见 [connections](connections.md)。主机首连批准使用 `JsonHostKeyStore.TrustNewAsync`，防止两个并发确认覆盖彼此保存的不同 key；这不改变独立信任库的失败关闭策略。

记住新秘密时，先把新引用写入持久化清理队列，再执行 CredWrite。变更将不再引用的旧凭据也先登记。随后提交配置：

- 失败 / 取消：重新读取实际已提交配置，删除未被引用的新凭据，保留仍被引用的旧凭据。
- 成功：发布新快照，删除已无 Server 引用的旧凭据，保留已被配置引用的新凭据。
- 删除 / 队列更新失败：提交仍成功，返回安全诊断，队列项保留；启动、显式重试、退出时再次核对。
- 中途进程退出：启动按已提交配置核对队列，不依赖内存中的“执行到哪一步”。

队列核对也受跨实例配置锁保护，另一实例不能删除正在准备提交的凭据。共享引用只有在最后一个 Server 不再引用时才删除。配置损坏、只读或暂时从备份恢复时，不执行不确定的凭据删除。队列自身损坏时不读取旧备份执行删除，新凭据操作在 CredWrite 之前失败。

配置备份可能引用已被删除的旧凭据。恢复旧配置后，需要用户重新输入这些秘密；备份不保存密码副本。

## 主机信任

`JsonHostKeyStore` 按规范化 host + SSH port + algorithm 查询 SHA256 指纹，保存 UTC 信任时间。重复身份、非法指纹、损坏文件或未知 schema 均失败；不会自动采用旧信任备份，以免恢复已经替换的旧信任。`TrustAsync` 只供后续明确批准 host key 的流程调用，配置服务不调用它。

## M9 删除事务

`ConfigurationService.CommitDeletionAsync` 在同一 FIFO 队列和文件锁内重新检查影响、等待相关停止并提交完整候选。停止 / 保存失败保留旧配置；配置发布后才清理无引用的凭据，共享引用保持有效。待清理凭据沿用持久队列和重试，发布后的文件异常按实际已提交状态处理。活动记录排在删除事务后写入，避免在持有配置队列时等待自身。故障用例见 `DeletionPersistenceTests`，完整契约见 [deletion](deletion.md)。

## 验证

M3 交付时为 Core 137 + Infrastructure 77，合计 **214 项通过、0 跳过**；Infrastructure 较 M2 新增 75 项。`STORE-01`–`STORE-04`、`CRED-01` 覆盖完整 round-trip、真实临时文件故障、迁移 / 恢复、并发 / 关闭、凭据补偿、重启清理和隔离的真实 Windows Credential Manager。

```powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Test.ps1
./scripts/Verify-Storage.ps1
./scripts/Verify-Window.ps1
```

窗口测试实际启动独立数据目录，验证未来版本保护、损坏 / 备份提示、外部修复后的 Retry、正常关闭不改写配置、侧栏 / 搜索与关闭等待期间的响应。证据见 `docs/evidence/2026-09-19-m3.json`，详细报告和截图位于 `artifacts/`。M2 的真实 SSH 18 项结果保留为历史证据；M3 未修改 SSH 传输并未重复运行该套测试。正式页面和 GUI 人工评测 A 从 M4 开始。
