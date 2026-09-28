# 导入与导出

M11 在 All Servers / All Profiles 的 More 中提供 Import / Export。两者都有独立页面；导入先预览，确认后一次保存。启动或导入本身不会建立 SSH 连接。

2026-09-23 UI 迭代：File 路径、JSON / SSH config 格式下拉框和 Browse 合并为一行。选中文件、编辑路径或切换格式会自动显示预览，不再需要点击 Preview。条目通过 SettingsExpander 展开行号和说明；认证方式与默认密钥来源放在 ItemsFooter。没有说明的条目使用紧凑卡片。

## TunnelDeck JSON

导出格式与应用内部的 state.json 分开，当前格式为：

- type：tunneldeck-export；schemaVersion：1；exportedAt：UTC 导出时间。
- servers：ID、显示名、Host、Port、Username、AuthMethod、可选 IdentityFilePath 路径提示。
- profiles：ID、Name、有序 Rules；每条规则包含原始端点、模式和 Local 自动查找选项。
- order：PinnedServerIds / OtherServerIds / PinnedProfileIds / OtherProfileIds，JSON 字段使用 camelCase。

Profiles 导出会附带所有依赖服务器的非秘密描述。可选择 Servers only、Profiles and required servers 或 All servers and profiles。

密码、私钥文件内容、CredentialReference、主机信任、活动隧道、最近活动、使用次数与应用设置均不导出。导出的路径只是提示，私钥文件不会被读取或打包。

导入全部新建 Server / Profile / Rule ID，并重映射 Profile 引用和顺序。重名及相同 endpoint 都保持为独立资源，不覆盖或合并已有服务器。原有 Pinned 顺序不动，新导入的 Pinned 追加；导入 Other 按文件顺序放在原 Other 之前；规则顺序保持不变。

JSON 采用全量校验：版本、必需字段、类型、重复属性、重复 ID、引用、端口、模式及顺序有任何错误都不提交。每条资源及规则的 ID 在文件中必须唯一。私钥认证需要本机存在的绝对文件路径；文件缺失时先修改源 JSON 中的路径，再重新预览，不能自动改成密码认证。

当前限制：UTF-8（允许 BOM）、8 MiB、JSON 深度 32、最多 5000 个资源及 50000 条规则。不支持直接导入 state.json、未知扩展字段或自动迁移未来 schema。空导出可以读取，但没有可提交的资源。

## SSH config 支持矩阵

| 内容 | 当前行为 |
| --- | --- |
| 具体 Host alias、同一行多个具体 alias | 每个别名成为一个服务器；重复别名合并解析，按首次出现排序 |
| Host *、首个 Host 之前的字段 | 作为默认值，不凭通配符生成服务器 |
| HostName / User / Port | 按文件顺序取首次匹配值；HostName 缺省使用 alias，Port 缺省 22，User 必须明确提供 |
| IdentityFile | 一个可解析且存在的路径按私钥认证导入；支持绝对路径与 ~/。无法表示或文件缺失时忽略该选项，明确提示使用密码认证并要求在 Server 详情中配置认证 |
| 大小写、空白、等号、单引号 / 双引号、注释 | 已用静态夹具与 Windows OpenSSH 9.5 对照；值中的 # 保留，独立注释标记后的内容忽略 |
| 未指定 IdentityFile | 使用 Settings 中的默认认证方式；默认密码认证保持原行为，默认密钥认证使用已配置的路径，路径未配置时检测用户目录下的 .ssh/id_rsa。预览明确显示来源 |
| Compression / ForwardX11 / ForwardX11Trusted / LocalForward / RemoteForward / 未支持选项 | 忽略该选项，逐条显示关键字和原文件行号；不阻止必要连接字段有效的服务器导入 |
| Include / ProxyJump / ProxyCommand | 忽略并提示；不读取 Include 文件、不执行命令、不启动代理或跳板连接，只导入预览显示的直接连接参数 |
| Match | 忽略 Match 及其后直到下一个 Host 的条件字段，不执行 Match exec；其字段不能补齐缺失的静态 User / HostName / Port |
| Host *、?、否定模式 | 静态匹配字段按 first-value 应用于文件中的具体别名；模式本身不创建服务器 |
| 多 IdentityFile、none、% token、环境展开、相对路径 | 列出行号和原因，忽略 IdentityFile，明确提示按密码认证导入 |
| 缺失 User、非法 HostName / User / Port、必要字段中的未解析 token | 条目不可用；不凭后续同名字段覆盖前面的非法首次取值 |

2026-09-21 用户确认调整导入规则：**仅当必要连接信息缺失或无效时跳过服务器；其他不支持的选项忽略并提示**。这取代 M11 初始的“未知选项导致整条跳过”规则。HostName 缺省仍使用具体 Host alias，Port 缺省仍使用 SSH 标准值 22，User 必须明确提供。缺失或非法 Host 声明的块被忽略，不把其中字段接到前一个服务器。

同日后续反馈增加认证默认值：有效的显式 `IdentityFile` 始终选择该文件并设置密钥认证，优先于全局默认。只有没有声明 `IdentityFile` 的条目才使用全局默认；`IdentityFile none`、无法表示的多路径或缺失文件仍显示原有忽略 / 密码回退提示，不用全局默认密钥替换该声明。全局默认选择密钥但没有可用默认文件时，该条目提示先在 Settings 配置密钥，不静默改成密码。提交前仍重新检查所有使用的密钥文件。

TunnelDeck JSON 中的 `authMethod` 是显式配置，不受这些默认值影响；已有服务器也不被导入或默认值修改覆盖。修改 Settings 后，旧导入预览需要重新生成。

不运行 ssh -G 解析用户文件，也不执行任何配置命令。真正包含不可用条目的预览必须明确勾选“Skip … unavailable entries”后才可提交其余资源；全被跳过时，页面明确说明没有可导入资源。单纯的忽略选项提示不会要求勾选 Skip。JSON 导入保持严格全量校验，不可用的 Profile 不会被拆成缺少规则的半个 Profile。

导入有警告时在列表显示 InfoBar；成功资源直接出现在列表。再次导入同一文件会创建新的副本。

## 提交、取消与退出

预览不写配置。修改输入路径、格式会取消旧请求、丢弃旧预览并自动重新读取；离开页面取消预览。保存状态的 revision 变化后自动更新预览，旧结果不可提交。手动编辑源文件后，可重新选择同一文件或在路径框按 Enter 重新读取；文件读取错误也提供 Retry。提交沿用 ConfigurationService 的 FIFO 原子事务，并在事务内经过 ServerWorkRegistry 的删除 / 退出准入检查，重新检查私钥路径。

导入只追加新 ID：不会重用被删除的身份、修改正在重连的隧道或自动启动导入的资源。删除在预览后发生会使该预览失效；导入使 Delete all 的影响变化时，删除必须重新确认。文件写入失败保留旧磁盘与内存状态，可从当前预览重试；发布边界之后的已成功写入不会被误报为失败。

导出使用系统保存对话框和同目录原子替换，只输出 .json，拒绝写入当前应用数据目录。可能留下空的 .lock 侧文件用于协调写入，分享时只需 JSON。读写任务在退出时取消并排空；配置导入的已接受提交由配置队列排空。

## 验证入口

scripts/Test.ps1 覆盖映射、严格 JSON、首次取值、错误及磁盘故障；scripts/Test.ps1 -Ssh 覆盖跨服务器 Profile 往返后的真实 Local / Remote payload、导入与重连 / 删除交错。scripts/debug/Verify-Transfer.ps1 覆盖实际文件选择器、预览取消、原子失败重试、跳过确认、Light / Dark、窄窗及重启。

系统文件选择器由 broker 进程托管。原生验证按应用所有权查找对话框 HWND；仅按应用 PID 枚举会漏掉它。测试驱动在 UIA 设置文件名后发送真实编辑按键，让 Shell 提交输入路径。
