# TunnelDeck

简洁的 Windows 11 / WinUI 3 SSH 端口转发助手。**M0–M8 已完成，GUI A / B / C / D 已确认；M9 已接入资源删除，GUI E 待评测**。选择服务器、输入端口并按 Enter 可快速启动；也可保存跨服务器规则组，一次启动并逐条查看结果、重试失败项。支持独立启停、Local 占用时递增、Requested / Actual、Recent、资源 Pin / 拖排、删除影响确认及失败重试。重连、导入导出等仍按后续里程碑推进。

## 构建与运行

在 PowerShell 7 中运行：

```powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Run.ps1
```

脚本使用项目 AGENTS.md 指定的 Visual Studio Developer PowerShell。项目仍为 `net10.0-windows10.0.19041.0`、x64、`WindowsPackageType=None`，直接启动：

```text
TunnelDeck/bin/x64/Release/net10.0-windows10.0.19041.0/TunnelDeck.exe
```

这是一条依赖已安装 .NET / Windows App Runtime 的开发运行路径，尚未验证干净机器发布。

## 验证

```powershell
# Core 与 Windows 凭据测试
./scripts/Test.ps1

# 自动启动并清理 WSL Ubuntu / Docker 中的真实 OpenSSH 夹具
./scripts/Test.ps1 -Ssh

# 已构建过夹具镜像时复用镜像
./scripts/Test.ps1 -Ssh -NoFixtureBuild

# 检查真实顶层窗口、侧栏滚动、标题栏输入与异步关闭
./scripts/Verify-Window.ps1

# 使用隔离数据检查未知版本、备份恢复和错误重试 UI
./scripts/Verify-Storage.ps1

# 六类页面、保存 / Pin、搜索 / Back、主题和窄窗交互
./scripts/Verify-Shell.ps1

# 隔离真实 SSH：凭据表单、指纹确认、重启、取消和关闭
./scripts/Verify-Connections.ps1 -NoFixtureBuild

# 隔离真实 SSH：Quick Forward、实际 payload、端口调整、重试和退出清理
./scripts/Verify-Forwards.ps1 -NoFixtureBuild

# 24 行活动列表、独立滚动、键盘详情、宽窄与 Light / Dark
./scripts/Verify-Dashboard.ps1

# Profile 草稿、跨服务器启动、部分失败与重试、真实 payload 和退出
./scripts/Verify-Profiles.ps1 -NoFixtureBuild

# 单个 / 全量删除、确认取消、保存失败重试、真实监听释放与关闭
./scripts/Verify-Deletions.ps1 -NoFixtureBuild

# Pinned 鼠标拖排、键盘等效操作、写入失败回退、搜索和重启
./scripts/Verify-Resources.ps1

# 查看布局预览，顶部可切换 Sample / Long lists / Empty
./scripts/Run.ps1 -Preview Sample

# 手动查看显式标注的窗口测试数据
./scripts/Run.ps1 -WindowProbe
```

SSH 夹具使用随机临时凭据、独立容器、仅绑定 loopback 的动态端口。测试结束删除容器和临时凭据；日志在 `.test-output/`，测试报告和截图在 `artifacts/`。测试不读取用户 SSH 配置，也不使用实际 TunnelDeck 用户数据。

## 数据与恢复

正常配置保存在 `%LOCALAPPDATA%\TunnelDeck\state.json`，原子替换时保留上一版 `.bak`。主文件损坏时可以加载有效备份并显示提示；二者都损坏时显示可重试错误，未知版本保持只读。正常编辑先保存成功再更新界面，退出等待已接收的修改。

密码 / 私钥口令存入 Windows Credential Manager，配置只保存 Guid；私钥仅引用路径。主机信任保存到独立的 `host-keys.json`。凭据清理失败由本地队列记录并在后续启动重试。恢复旧配置后，旧凭据可能需要重新输入。完整约定见 [storage](docs/storage.md)。

窗口 probe 自动使用临时目录和隔离凭据命名空间；需要指定诊断数据目录时使用 `./scripts/Run.ps1 -DataDirectory <absolute path>`。

`-Preview Sample|Stress|Empty` 同样隔离数据，不建立 SSH 连接。网络命令明确禁用；保存、Pin、菜单移动和基础设置可以实际使用。普通模式的真实转发用法、临时凭据与历史记录约定见 [quick-forward](docs/quick-forward.md)，页面 / 导航见 [shell-and-navigation](docs/shell-and-navigation.md)。

Profile 编辑使用独立草稿，Apply rule 不直接写盘；Save 一次保存名称、规则和顺序。只有已保存且未修改的 Profile 可启动，现有隧道继续使用原快照。每次 Profile 启动操作至少有一个新实例成功才计一次使用；纯重复、访问页面和 Pin 不计数。详见 [profiles](docs/profiles.md)。

Delete / Delete all 位于 More。Server 删除先展示影响，取消相关待启动工作并停止转发，再一次提交配置；停止或保存失败时保留配置，已停止的转发保持停止并可重新检查后重试。Profile 删除保留服务器、已有转发和来源快照。详见 [deletion](docs/deletion.md)。

## 当前技术边界

- SSH.NET 固定为 `2026.0.0`。Local 规则可共享连接；Remote 规则各自使用独立 SSH 传输，避免上游全局回复串扰。
- 未知 host key 先拒绝握手，原生对话框确认后重新校验并认证；已保存指纹变更时拒绝连接。Test Connection 使用当前草稿，临时连接在成功返回前关闭，不生成活动隧道。详见 [connections](docs/connections.md)。
- 多条规则首次连接同一指纹时共用一次确认，Remote 的物理连接仍独立；取消一个请求不会取消其他请求的确认。
- 未保存密码时在启动前输入本次凭据，Quick 不缓存或保存秘密；已有 Local 传输可以复用。固定版本的转发通道存活探测兼容处理见 [ADR 0002](docs/decisions/0002-forwarded-channel-keepalive.md)。
- 自动递增仅用于 Local 本地监听端口的确认占用。远端占用、禁止转发和 PermitListen 拒绝均产生通用错误；按用户确认的范围，**Remote 不自动递增**，失败后停止并释放传输，等待手动处理。
- 超时或取消不复用旧传输；Remote Stop 的应用截止时间必须短于库的内部超时。
- 监听建立不代表目标服务健康。请求错误单独聚合；SSH.NET 的 Local 目标拒绝通知不完整，不能据此保证每个目标故障都出现在应用中。M6 的 1 MiB payload、并发客户端、取消与监听释放已有真实 SSH 验证，详见 [tunnels](docs/tunnels.md)。

完整命令和环境见 [baseline](docs/baseline.md)，当前进度见 [milestones](docs/milestones.md)，M2 契约见 [domain-model](docs/domain-model.md)，关键变更依据见 [SSH 控制决策](docs/decisions/0001-ssh-control-isolation.md)。产品规范保存在 [.codex/docs](.codex/docs/implementation-plan.md)。
