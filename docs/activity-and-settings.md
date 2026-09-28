# Recent、Statistics 与 Settings

M12 完成 Dashboard 辅助区域与自动保存设置。Recent 保留在 Dashboard 下部，默认显示最近 5 条，可展开全部已保留记录；最多 200 条，在有高度上限的原生 ListView 中滚动，不新增 History 页面。宽窗口与 Statistics 并排，窄窗口上下排列。

## 活动与计数

| 事件 | Total forwards / Server forwards | Profile uses |
| --- | --- | --- |
| 新实例首次成功建立监听 | 增加 1 | 该次 Profile 操作第一次有新成功时增加 1 |
| 重复启动返回已有实例 | 不增加 | 只有已有实例时不增加 |
| 自动重连恢复同一实例 | 不增加 | 不增加 |
| 停止后重新启动 | 成功后增加 1 | 按新操作计算 |
| 失败、取消、浏览页面、测试连接、打开终端 | 不增加 | 不增加 |

累计计数独立持久化。Recent 裁剪、删除 Server / Profile、配置导入、重启不清零计数；资源使用频率也不改变 All 页 Other 顺序。活动写入失败仍使用已有的 Retry saving activity 队列，统计只反映已保存的配置快照。

Recent 按事件发生时间倒序保留最新 200 条，较迟到达的旧事件不能挤掉较新历史，但仍按其真实启动事件更新累计次数。同一时间的事件保持最新接收到的记录在前。事件时间以 UTC 保存，相对时间和完整时间按本机时区显示；完整时间包含 UTC offset，可区分夏令时回拨时的重复本地时间。

每条记录保留当时的 Server / Profile 名称、方向、Requested / Actual 地址端口和安全错误。重命名与删除只增加来源说明，不覆盖历史名字。同名的不同资源按 ID 区分。悬停或无障碍名称可读取完整路径及时间，默认列表保持紧凑。

相对时间每分钟在已加载的 Dashboard 中刷新，离开或卸载页面即停止 timer；刷新时间、查看详情和展开历史都不写磁盘。

## 统计展示

Total forwards 是包含已删除资源的累计成功启动数。Most used server / profile 只在**当前仍存在且使用次数大于零**的资源中排名，显示次数；相同次数按 ID 排序，名称重复时补充短 ID。页面明确显示当前排名与历史总量的区别，删除最高项后可显示下一项，而不是被已删除 ID 挡住。

不展示尚未实现的传输流量或实时带宽，不把请求数当作成功连接数，也不用零值占位未实现的指标。

## 设置

- Language 提供 English / 简体中文，保存成功后即时更新当前窗口，无需重启。默认英语，不自动跟随系统语言；实现和验证见 [多语言](localization.md)。
- Sidebar maximum 范围 3–10，只接受整数。
- 默认 Server 可选 None，删除该 Server 后由删除事务清空引用。它与 Quick 当前选择、Quick 固定选择及 All 页列表 Pin 分开。
- 自动查找本地端口是新 Local Quick / Rule 的默认值。窗口中已存在的 Quick 草稿与保存的 Rule 保持原值；重启后的新 Quick 使用新默认。Remote 不使用此设置。
- Server authentication 提供默认 Password / Private key。只有 Private key 时显示默认密钥选项；首次选择时检测 `%USERPROFILE%\.ssh\id_rsa`，也可浏览选择其他文件或输入完整路径。文件选择后立即保存，手输路径用 Enter 或 Save path 保存。
- 默认认证仅预填新 Server，以及没有声明 IdentityFile 的 SSH config 条目；不改写已有 Server、正在编辑的草稿或 JSON 导出中的显式认证。显式 IdentityFile 优先。密码模式保留已选默认密钥路径，供再次切回密钥时使用，但不会把路径复制进新的密码 Server。
- 没有检测到 id_rsa 时可选择其他文件；新 Server 的密钥草稿保持可编辑。默认密钥不可用时，导入预览要求先修正 Settings，不默默切换认证方式。设置只保存路径，不保存默认密码、口令或私钥内容。

每次修改沿 ConfigurationService 的 FIFO 队列即时保存，只修改相应字段，基于最新状态执行，不覆盖同时发生的活动、导入或其他设置。相同值不写磁盘，也不使已有导入预览失效。非法引用或数值不保存；磁盘失败恢复界面到已保存值，Settings 底部显示错误及 Retry saving，重试仅重新提交该次设置修改。

Appearance 的 Window theme 延续现有窗口级设置，可用于 Light / Dark 评测；本里程碑未扩展持久化主题模型。

内部配置现使用 schema 3，保存 `language`、`defaultAuthenticationMethod` 和 `defaultIdentityFilePath`。schema 1 / 2 在内存中逐步补充缺少的认证默认值和英语语言设置，已有资源 / 凭据引用 / 顺序 / 历史 / 计数保持不变；加载不写盘，第一次实际编辑才保存新格式并保留旧备份。旧版应用会把新格式视为不支持，不能回退旧备份覆盖它。可移植 TunnelDeck JSON 格式仍为 v1。

## 验证

`ActivityPresentationTests` 使用假时钟覆盖分钟、小时、跨日、未来时间和 DST offset；覆盖名称快照、Remote 方向、同名区分及删除后的统计排名。`ActivityRetentionTests` 覆盖较晚到达的旧事件、200 条裁剪、重复启动、停止后新启动及新旧 Quick 草稿默认值。`SettingsAndActivityPersistenceTests` 覆盖真实文件重启、UTC、累计计数、3/10 边界、缺失 Server 引用、并发设置、失败重试与相同值不写入。

已有 TunnelWorkspace、Reconnect、ProfileRunner 与删除测试继续验证统一计数口径。原生入口 `scripts/debug/Verify-Activity.ps1` 验证辅助布局、展开、设置错误和重试、重启及改进后的 SSH config 导入；`scripts/debug/Show-ActivityReview.ps1` 打开带 200 条样例历史的隔离 GUI H 窗口。样例使用保留的 .example 主机名，不自动建立连接。

认证默认值另由 `ServerAuthenticationDefaultsTests`、`AuthenticationDefaultsPersistenceTests`、`AuthenticationDefaultsForwardingTests` 和 `scripts/debug/Verify-AuthenticationDefaults.ps1` 覆盖默认来源、显式覆盖、真实密钥登录与转发、原生文件选择 / 取消、保存失败重试、旧格式兼容及重启预填。

`scripts/debug/Show-AuthenticationReview.ps1` 直接打开隔离数据中的认证设置，方便评测新选项；普通用户设置保持原值。本轮 663 项测试、认证原生与存储回归结果，以及 Transfer 缩窗时复现的已知渲染异常，见 [认证默认值证据](evidence/2026-09-21-m12-authentication-defaults.json)。
