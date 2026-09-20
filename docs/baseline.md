# M0 开发基线

记录日期：2026-09-19。原始模板基于 Git `2039b14f9f83616748b2471f343defd1d58710f7`；本次工作分支 `codex/m0-m1-foundation`。

## 实际环境

| 项目 | 实测值 |
| --- | --- |
| 操作系统 | Windows 11，10.0.26200，x64 |
| CPU | AMD Ryzen 9 5900X，12 核 / 24 逻辑处理器 |
| Visual Studio | 2026 Insiders 18.9.0 |
| .NET SDK | 10.0.400-preview.0.26322.102，已写入 global.json |
| .NET Runtime | 10.0.9 |
| App 目标框架 | net10.0-windows10.0.19041.0 |
| Windows App SDK | 2.5.1，WinUI 子包 2.3.9 |
| Windows SDK BuildTools | 10.0.26100.4654 |
| 启动模型 | WindowsPackageType=None，直接 exe，框架依赖 |
| SSH.NET | 2026.0.0，源码提交 7b2fd3dbf2c86a80a7b06cea020aa5f821c9902e |
| 测试框架 | xUnit 2.9.3，VSTest adapter 2.8.2，Test SDK 17.13.0 |
| WSL | Ubuntu 22.04，Docker Engine 29.1.3 |
| 隔离 sshd | OpenSSH 8.9p1 / Ubuntu 1:8.9p1-3ubuntu0.17 |
| GUI 自动化 | winapp CLI 0.3.1 的 ui 命令 |

沿用模板现有项目名、目标框架、包引用和启动模型。没有运行机器安装/升级脚本。NuGet 的直接和传递依赖由各项目 `packages.lock.json` 锁定。

解决方案各项目显式映射 x64；NuGet 中间文件按 Platform 隔离。恢复过程中曾发现旧 `obj/project.assets.json` 含 NUL 数据，已恢复并隔离不同平台的还原目录；后续使用文档中的统一 x64 入口。

M5 在 App 项目中显式声明与既有锁文件一致的 `RuntimeIdentifiers=win-x86;win-x64;win-arm64`，解决包导入状态变化时锁定还原报 NU1004 的问题。仍只构建 x64，未变更包版本、输出目录或直接 exe 的启动模式。

## 重复执行的入口

```powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Test.ps1
./scripts/Test.ps1 -Ssh
./scripts/Verify-Window.ps1
./scripts/Verify-Storage.ps1
./scripts/Verify-Shell.ps1
./scripts/Verify-Connections.ps1 -NoFixtureBuild
./scripts/Run.ps1
```

`Build.ps1` / `Test.ps1` 进入以下开发环境后执行 dotnet：

```powershell
$cwd = $PWD.Path
& 'D:\Visual Studio\18\insiders\Common7\Tools\Launch-VsDevShell.ps1' -Arch amd64 -HostArch amd64
Set-Location -LiteralPath $cwd
dotnet build TunnelDeck.slnx -c Release -p:Platform=x64 -p:RestoreLockedMode=true
```

App 路径为 `TunnelDeck/bin/x64/Release/net10.0-windows10.0.19041.0/TunnelDeck.exe`。`Run.ps1` 以非零窗口句柄、进程未退出和窗口响应作为成功标准。`Verify-Window.ps1` 会关闭自己启动的 probe 窗口；正常 `Run.ps1` 保留窗口供使用。

M4 新增 `Run.ps1 -Preview Sample|Stress|Empty` 与 `Verify-Shell.ps1`。GUI 脚本调整窗口时显式进入 PerMonitorV2 DPI 上下文，避免 PowerShell 的坐标虚拟化重复放大；M4 的 900 / 640 / 500 epx 结果按此测得。历史记录中的窗口尺寸保留原始口径，不用它们替代本次尺寸验证。截图优先捕获目标窗口渲染。

## SSH 夹具

`test-infra/sshd/Dockerfile` 固定 Ubuntu 镜像摘要 `sha256:b8b6ee6aa931ecd9d0d952abc34dc0e5f7c6a30c6bb71b079fe399fde0329c02` 和 OpenSSH 包版本。初次构建需要访问 Docker Hub / Ubuntu 包仓库；后续可使用 `-NoFixtureBuild`。固定包若从上游移除应明确升级并重新验证，不自动换版本。

一个临时容器内有四个 sshd：允许转发、禁止转发、PermitListen 限制、可更换 host key。另有 IPv4/IPv6 echo 服务和带随机 token 的控制入口。所有对主机的发布端口仅绑定 `127.0.0.1`；远端监听直接从容器内部传输 payload 来检验。

```powershell
$fixture = ./test-infra/Start-SshFixture.ps1
$env:TUNNELDECK_SSH_FIXTURE = $fixture
try {
    . ./scripts/Enter-DevelopmentShell.ps1
    dotnet test tests/TunnelDeck.Ssh.Tests/TunnelDeck.Ssh.Tests.csproj -c Release -p:Platform=x64
}
finally {
    ./test-infra/Stop-SshFixture.ps1 -ManifestPath $fixture
    Remove-Item Env:TUNNELDECK_SSH_FIXTURE -ErrorAction SilentlyContinue
}
```

优先使用 `Test.ps1 -Ssh`，它将建立、测试、清理放在同一受控生命周期中。WSL 的 systemd 服务本身不能保证发行版一直驻留；启动脚本另开隐藏的、仅等待本容器退出的 `docker wait` 进程。Stop 校验容器标签与 run ID 后只移除该容器，等待进程随容器退出。WSL root 的 systemd 用户 session 偶有诊断提示；以 Docker 命令和真实服务就绪检查判定夹具状态。

临时口令、加密私钥和 manifest 仅在 `.test-output/<run>/`，该目录被 Git 忽略。输出目录必须为空，避免读取旧 manifest。Stop 删除凭据文件，保留不含口令的 sshd 日志。Windows Credential Manager 测试仅使用随机 `TunnelDeck/Tests/<guid>` 命名空间并在 finally 中删除。

## 原始模板实测

首次修改 UI 前：Release x64 构建成功，0 warning / 0 error；实际出现标题为 `MainWindow` 的响应窗口。

| 指标 | 原始模板 |
| --- | --- |
| 空闲采样长度 | 60.034 秒 |
| 进程 CPU 增量 | 0.03125 CPU 秒 |
| 按 24 逻辑处理器归一的机器 CPU | 0.00217% |
| Private Bytes | 95,776,768 bytes，约 91.3 MiB |
| Working Set | 129,523,712 bytes，约 123.5 MiB |
| 句柄 / 线程 | 1066 / 77 |

采样是本机空闲窗口的单次基线，不是长期性能承诺。原始模板的冷启动耗时未单独采样。新增窗口 probe 实测启动至非零窗口句柄约 770 ms；750 ms 模拟异步清理期间保持响应，重复关闭后约 902 ms 退出、退出码 0。此数据用于证明窗口行为，不能冒充包含真实 SSH 清理的完整 App 关闭性能。

原始数据：`artifacts/baseline/template-release.json`、`artifacts/window-verification/result.json`；本次保留摘要见 `docs/evidence/2026-09-19-m0-m1.json`。

## 验证边界

- 本机 Release 构建和真实窗口均已验证；未验证干净 Windows 安装或正式发布。
- Light / Dark 与宽窗 / 短窗已实测。M4 / M5 的 GUI A / B 已由用户确认；Agent 未切换系统 High Contrast，系统文字放大、多显示器 DPI 与完整辅助功能验收仍留待 M13。
- winapp 0.3.1 不包含技能 0.6.x 文档中的 find-ui/find-api。API 依据项目实际包、其 generic.xaml、Microsoft Learn 和固定版本 SSH.NET 源码核对；没有因工具版本差异升级整套环境。
