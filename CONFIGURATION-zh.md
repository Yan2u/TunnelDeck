# TunnelDeck — 配置和用法

[English](CONFIGURATION.md) | 简体中文

项目简介与界面截图见 [README](README-zh.md)。本文保留详细使用说明、构建与发布流程、验证入口以及历史验收记录。

历史验证（2026-09-23）：功能与主要 UI / UX 通过用户评测，Release x64 本地发布收尾完成。UI 项目位于 `src/TunnelDeck`；设计规则见 [统一设计](docs/ui-design-system.md)，回归结果见 [UI 验证](docs/ui-refresh-validation.md)，内存分析见 [性能记录](docs/performance.md)。此后加入了英语 / 简体中文支持和 schema 3，详见 [多语言](docs/localization.md)。下方脚本目录反映 2026-09-28 整理后的结构。

简洁的 Windows 11 / WinUI 3 SSH 端口转发助手。选择服务器、输入端口并按 Enter 可快速启动；也可保存跨服务器规则组，一次启动并逐条查看结果、重试失败项。支持独立启停、Local 占用时递增、Requested / Actual、200 条 Recent、累计统计、设置自动保存、资源 Pin / 拖排、导入导出、独立终端、删除影响确认、断线恢复、有界退出与单实例。M0–M13 的历史验收见 [M13 验证记录](docs/m13-validation.md)，当前包的运行库与数据升级说明见 [本地发布](docs/release.md)。

## 构建与运行

在 PowerShell 7 中运行：

```powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Run.ps1
```

也可在 Visual Studio 中打开根目录的 `TunnelDeck.slnx`，选择 `Debug | x64` 或 `Release | x64` 构建。UI 项目的 `Build/XamlIncrementalBuild.targets` 独立记录实际 XAML 编译的输入，在 XAML、项目路径或构建规则变化时使旧编译缓存失效，避免设计时生成的 `.g.i.cs` 与旧 `.g.cs` 混用；输入未变时保留增量构建。

脚本使用项目 AGENTS.md 指定的 Visual Studio Developer PowerShell。项目仍为 `net10.0-windows10.0.19041.0`、x64、`WindowsPackageType=None`，直接启动：

```text
src/TunnelDeck/bin/x64/Release/net10.0-windows10.0.19041.0/TunnelDeck.exe
```

这是一条依赖已安装 .NET / Windows App Runtime 的开发运行路径，尚未验证干净机器发布。

## 仓库结构与脚本

| 目录 | 用途 |
| --- | --- |
| [scripts/](scripts/README.md) | 六个日常入口：Build、Run、Test、Publish-Release、Clean-GeneratedArtifacts、Enter-DevelopmentShell。 |
| [scripts/debug/](scripts/debug/README.md) | UI 回归、评测窗口、性能工具及共享辅助代码。 |
| [scripts/fixtures/](scripts/fixtures/README.md) | Start-SshFixture / Stop-SshFixture，以及配套 Dockerfile / Python SSH 测试服务。 |
| [scripts/packaging/](scripts/packaging/README.md) | 随发布包分发的 Check-Prerequisites 和 Smoke-Test。 |
| [tests/](tests) | 需要编译的 Core、Infrastructure 和 SSH 测试项目。 |
| [test-infra/TunnelDeck.ProcessProbe/](test-infra/TunnelDeck.ProcessProbe) | 供集成测试检查进程参数的编译型辅助程序。 |

文档命令均从仓库根目录运行。`Test.ps1 -Ssh` 自动管理夹具生命周期；单独使用时，入口为 `scripts/fixtures/Start-SshFixture.ps1` 和 `scripts/fixtures/Stop-SshFixture.ps1`。测试输出仍位于 `.test-output/` 和 `artifacts/`。

## Release x64 发布

Visual Studio 的 UI 项目使用 `ReleaseX64` 发布配置；自动打包使用：

```powershell
./scripts/Publish-Release.ps1 -Verify
```

默认生成 `artifacts/release/TunnelDeck-<项目版本>-win-x64.zip`、SHA-256 校验值和文件清单。脚本锁定依赖，检查编译后的 XAML 资源，并从 ZIP 解压后运行启动、编辑、旧配置升级与重启检查。发布包依赖共享运行库，本地未签名；没有自动上传或创建外部 Release。

源码中的 `scripts/packaging/Check-Prerequisites.ps1` 和 `scripts/packaging/Smoke-Test.ps1` 会被复制到发布包根目录，与 `TunnelDeck.exe` 放在一起。包内文件名和命令不变；`docs/release.md` 作为包内 README。`-Verify` 调用 `scripts/debug/Verify-Release.ps1`，由它执行解压后的 smoke 脚本。

验证新的 schema 3 发布包前，需先更新 `Publish-Release.ps1` 中既有的 schema 2 元数据及 `Smoke-Test.ps1` 中的升级断言。本次目录整理验证了脚本路径、包内文件复制和运行库检测，未重跑 GUI smoke，也未生成新发布包。

历史包 [TunnelDeck-1.0.0-win-x64.zip](artifacts/release/TunnelDeck-1.0.0-win-x64.zip) 构建于 2026-09-23，早于多语言功能。该包验收摘要见 [收尾证据](docs/evidence/2026-09-23-release-closeout.json)；最新功能请使用当前源码构建。

## 验证

日常脚本及依赖说明见 [脚本目录](scripts/README.md)。UI 回归、评测样例和性能工具集中在 [scripts/debug](scripts/debug/README.md)，按需运行。

部分历史 UI 脚本仍依赖早期文案、布局或 schema 版本，针对当前应用运行前需先适配这些假设；历史验收结果不代表当前版本已通过全量回归。

```powershell
# Core 与 Windows 凭据测试
./scripts/Test.ps1

# 自动启动并清理 WSL Ubuntu / Docker 中的真实 OpenSSH 夹具
./scripts/Test.ps1 -Ssh

# 已构建过夹具镜像时复用镜像
./scripts/Test.ps1 -Ssh -NoFixtureBuild

# 查看布局预览，顶部可切换 Sample / Long lists / Empty
./scripts/Run.ps1 -Preview Sample

# 手动查看显式标注的窗口测试数据
./scripts/Run.ps1 -WindowProbe
```

SSH 夹具使用随机临时凭据、独立容器、仅绑定 loopback 的动态端口。测试结束删除容器和临时凭据；日志在 `.test-output/`，测试报告和截图在 `artifacts/`。测试不读取用户 SSH 配置，也不使用实际 TunnelDeck 用户数据。

M12 / M13 基线：663 项测试通过，Release x64 锁定构建零警告、零错误。历史导入缩窗异常已在 M13 定位为焦点切换期间重置列表，修正后完整 Transfer 与循环检查通过。规模、性能、原生回归及仍需验收的项目见 [M13 记录](docs/m13-validation.md)；[M12 原始证据](docs/evidence/2026-09-21-m12-authentication-defaults.json) 保留原来的失败事实。

## 数据与恢复

正常配置保存在 `%LOCALAPPDATA%\TunnelDeck\state.json`，原子替换时保留上一版 `.bak`。主文件损坏时可以加载有效备份并显示提示；二者都损坏时显示可重试错误，未知版本保持只读。正常编辑先保存成功再更新界面，退出等待已接收的修改。

内部配置为 schema 3，旧配置加载时在内存补充缺少的认证默认值和英语语言设置，首次实际编辑才保存新格式并保留旧备份。可移植导出格式仍为 v1。

在 Settings → Appearance → Language（设置 → 外观 → 语言）中选择 English / 简体中文，保存后即时切换，无需重启。默认英语，不自动跟随系统；详见 [多语言](docs/localization.md)。

密码 / 私钥口令存入 Windows Credential Manager，配置只保存 Guid；私钥仅引用路径。主机信任保存到独立的 `host-keys.json`。凭据清理失败由本地队列记录并在后续启动重试。恢复旧配置后，旧凭据可能需要重新输入。完整约定见 [storage](docs/storage.md)。

窗口 probe 自动使用临时目录和隔离凭据命名空间；需要指定诊断数据目录时使用 `./scripts/Run.ps1 -DataDirectory <absolute path>`。

`-Preview Sample|Stress|Empty` 同样隔离数据，不建立 SSH 连接。网络命令明确禁用；保存、Pin、菜单移动和基础设置可以实际使用。普通模式的真实转发用法、临时凭据与历史记录约定见 [quick-forward](docs/quick-forward.md)，页面 / 导航见 [shell-and-navigation](docs/shell-and-navigation.md)。

Profile 编辑使用独立草稿，Apply rule 不直接写盘；Save 一次保存名称、规则和顺序。只有已保存且未修改的 Profile 可启动，现有隧道继续使用原快照。每次 Profile 启动操作至少有一个新实例成功才计一次使用；纯重复、访问页面和 Pin 不计数。详见 [profiles](docs/profiles.md)。

Delete / Delete all 位于 More。Server 删除先展示影响，取消相关待启动工作并停止转发，再一次提交配置；停止或保存失败时保留配置，已停止的转发保持停止并可重新检查后重试。Profile 删除保留服务器、已有转发和来源快照。详见 [deletion](docs/deletion.md)。

已建立的转发在瞬时断线后自动重连：每台服务器一个恢复组，1 秒起指数退避到 30 秒，总窗口 2 分钟，优先恢复上次实际本地端口；认证 / host key 等永久错误等待手动 Retry，Stop / Delete / 退出期间不复活。关闭窗口即退出：先禁用新命令，再以 10 秒预算清理并保存，失败时提供 Retry 或明确的不保存退出。同一数据目录只运行一个实例，再次启动会还原已有窗口。详见 [lifecycle](docs/lifecycle.md)。

## 导入、导出与终端

Import / Export 位于 All 页的 More。Profile 导出包含所需服务器，JSON 不包含秘密、信任或运行状态；导入新建 ID，不按名称覆盖，非法 JSON 整批拒绝。SSH config 的必要连接字段有效即可导入，其他不支持选项忽略并提示；缺失 / 非法必要字段才需补齐或跳过，不回写本地 SSH config。详见 [导入导出](docs/transfer.md)。

Settings → Server authentication 可设置默认 Password / Private key。选择密钥后显示默认文件，自动检测 `%USERPROFILE%\.ssh\id_rsa`，支持浏览选择或手输路径。新建 Server 和未声明 `IdentityFile` 的 SSH config 条目使用这些默认值；有效的显式 `IdentityFile` 优先，已有 Server 与 JSON 中的显式认证保持原值。

Recent 保留事件发生时的来源名称，删除 / 改名后仍可辨认；统计总量不受 Recent 裁剪影响，Most used 只排名当前资源。端口默认值只影响新的 Quick / Rule，认证默认值只预填新的 Server / SSH 导入，已有草稿保留；详见 [活动与设置](docs/activity-and-settings.md)。

Quick Forward 的 Open terminal 按钮与 Server 详情的 More 可打开独立 SSH 终端。终端自己处理认证和 known_hosts，不接收已保存密码；退出应用与关闭终端互不影响。详见 [Open Terminal](docs/terminal.md)。

## 清理生成文件

测试结束并保留必要验收摘要后，可预览并清理工作区内的旧日志、截图、临时夹具和构建目录：

```powershell
./scripts/Clean-GeneratedArtifacts.ps1 -IncludeBuildOutputs -WhatIf
./scripts/Clean-GeneratedArtifacts.ps1 -IncludeBuildOutputs
```

脚本保留 `artifacts/release` 中的发布包与验证摘要、源码、`docs/evidence` 以及 Visual Studio 设置。删除前检查绝对路径、重解析点、已跟踪文件和占用进程；SSH 夹具需先正常停止。清理构建输出后，运行开发版本前需重新构建；发布包可直接使用。

## 当前技术边界

- SSH.NET 固定为 `2026.0.0`。Local 规则可共享连接；Remote 规则各自使用独立 SSH 传输，避免上游全局回复串扰。
- 未知 host key 先拒绝握手，原生对话框确认后重新校验并认证；已保存指纹变更时拒绝连接。Test Connection 使用当前草稿，临时连接在成功返回前关闭，不生成活动隧道。详见 [connections](docs/connections.md)。
- 多条规则首次连接同一指纹时共用一次确认，Remote 的物理连接仍独立；取消一个请求不会取消其他请求的确认。
- 未保存密码时在启动前输入本次凭据，Quick 不缓存或保存秘密；已有 Local 传输可以复用。固定版本的转发通道存活探测兼容处理见 [ADR 0002](docs/decisions/0002-forwarded-channel-keepalive.md)。
- 自动递增仅用于 Local 本地监听端口的确认占用。远端占用、禁止转发和 PermitListen 拒绝均产生通用错误；按用户确认的范围，**Remote 不自动递增**，失败后停止并释放传输，等待手动处理。
- 超时或取消不复用旧传输；Remote Stop 的应用截止时间必须短于库的内部超时。
- 监听建立不代表目标服务健康。请求错误单独聚合；SSH.NET 的 Local 目标拒绝通知不完整，不能据此保证每个目标故障都出现在应用中。M6 的 1 MiB payload、并发客户端、取消与监听释放已有真实 SSH 验证，详见 [tunnels](docs/tunnels.md)。

完整命令和环境见 [baseline](docs/baseline.md)，当前进度见 [milestones](docs/milestones.md)，M2 契约见 [domain-model](docs/domain-model.md)，关键变更依据见 [SSH 控制决策](docs/decisions/0001-ssh-control-isolation.md)。产品规范保存在 [.codex/docs](.codex/docs/implementation-plan.md)。
