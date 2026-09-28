# TunnelDeck Release x64

Windows 11 / WinUI 3 SSH 端口转发助手。当前发布配置固定为 Release、x64、win-x64，ZIP 名称默认使用项目版本号；包中包含程序、资源、运行库检查脚本和文件散列清单。

这是 Windows 11 x64 的 unpackaged 多文件程序。它依赖共享运行库，不是单文件免安装版；本地包未签名，也未发布到商店或 GitHub Release。

## 首次运行

1. 将 ZIP 完整解压到可写的独立目录，保留所有 DLL、子目录和资源文件。
2. 运行 `Check-Prerequisites.ps1` 检查本机运行库。此脚本只读检查，不安装软件或修改设置。
3. 缺少依赖时，从下方官方入口安装 x64 版本，再检查一次。
4. 双击 `TunnelDeck.exe`。创建 Server，填写 Host、Username、Port，选择密码或私钥认证，然后使用 Quick Forward 或 Profile 启动转发。

可用 Windows PowerShell 5.1 执行 `Smoke-Test.ps1` 自动验证隔离的启动、真实 UI 编辑、旧数据升级、备份与重启。它只使用新建临时目录和 `release.example` 样例，不连接服务器或修改正常用户数据；需要可交互桌面，无需安装 .NET SDK 或 winapp。提供 `-PreviousExecutable <旧版完整路径>` 时，还会验证旧版对新 schema 的只读保护，以及从单独旧备份回退。

| 依赖 | 要求与官方入口 |
| --- | --- |
| Windows | Windows 11 x64 |
| .NET | [.NET Runtime 10.0.x x64](https://dotnet.microsoft.com/download/dotnet/10.0)，不要求安装 SDK |
| Windows App Runtime | 2.5.1，或兼容的较新 2.x x64；[Windows App SDK 下载与版本](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) |
| Visual C++ | [Microsoft Visual C++ Redistributable x64](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) |
| 外部终端，可选 | Windows OpenSSH Client；Windows Terminal 可选，缺少时使用系统 SSH 控制台 |

Windows App Runtime 的 unpackaged 部署与 bootstrap 约定见 [Microsoft 部署指南](https://learn.microsoft.com/windows/apps/windows-app-sdk/deploy-unpackaged-apps)。不要仅复制 exe，也不要用随机 DLL 下载站补文件。

## 转发与认证

- Local forwarding 在本机监听，目标地址从 SSH 服务器一侧解析；常用于从本机访问服务器上的 Web / 数据库服务。
- Remote forwarding 在 SSH 服务器监听，目标地址从本机一侧解析；服务端必须允许远程转发。拒绝时不会自动换端口。
- 自动找端口仅处理确认的本地监听占用；界面保留 Requested 与 Actual。Running 表示监听已建立，不保证目标服务正在运行。
- Settings → Server authentication 可设置新 Server 的默认认证。密钥模式自动检测 `%USERPROFILE%\.ssh\id_rsa`，也可选择其他文件。SSH config 的有效显式 `IdentityFile` 优先于默认值。
- 密码和私钥口令由 Windows Credential Manager 保存；私钥内容不复制到配置或导出。首次 SSH 主机指纹需要明确确认，已保存指纹改变时拒绝连接。

## 数据、升级和回退

正常数据位于 `%LOCALAPPDATA%\TunnelDeck`，与程序目录分开。配置原子保存并保留 `state.json.bak`；主机信任与凭据清理队列是单独的文件。JSON 导出用于迁移资源，不含密码、主机信任、活动、统计或默认设置。

升级时先正常关闭旧版，备份整个数据目录，将新版本解压到另一个目录后运行。同一数据目录只能运行一个实例。卸载只需删除程序目录；不会自动删除数据目录或 Credential Manager 中的密码。

当前内部配置为 schema 2。schema 1 加载时在内存补充密码默认值，首次实际编辑才保存新格式；旧版看到较新的 schema 会进入只读模式，不能回退备份覆盖新数据。回退程序前保留当前数据，并在需要旧格式时恢复**升级前的独立备份**；恢复备份会放弃之后的更改，凭据可能需要重新输入。

正常退出会停止转发并保存历史，失败时提供重试或明确的不保存退出。网络中断时远端监听的释放仍取决于服务器检测断线的时机。

## 已知兼容性与验证边界

TCP half-close、长期高负载通道回收和所有第三方图形叠加层的组合不在当前已验证范围内。关闭窗口即退出，未实现托盘常驻。

本地运行与数据升级 / 回退的 smoke 结果、UI 回归和性能数值以随版本交付的验证记录为准。干净 Windows 11 上的依赖安装、真正的多显示器 DPI 切换、200% 系统文字完整流程以及 Narrator 实际朗读未在本轮自动验证中覆盖。

`package-manifest.json` 记录文件散列、基线 commit 和未提交状态。整个 ZIP 的散列在随包交付的发布记录中；文件散列用于检查传输完整性，不等于发布者签名。

## 从源码发布

在 Visual Studio 中选择 UI 项目的 `ReleaseX64` 发布配置，或在仓库根目录的 PowerShell 7 中运行：

```powershell
./scripts/Publish-Release.ps1 -Verify
```

脚本固定使用 Release x64、锁定依赖版本，并检查当前项目所有 XAML 的编译资源。默认输出到 `artifacts/release`，生成版本命名的目录、ZIP、SHA-256 文件与 `package.json`；`-Verify` 会解压到含空格和中文的路径，核对散列并运行隔离启动、编辑和数据升级检查。已有同名包不会被覆盖，可用 `-OutputDirectory` 或 `-BuildLabel` 指定新的输出。

配套脚本源码位于 `scripts/packaging/`；发布时仍复制到包根目录，本文的包内命令不变。
