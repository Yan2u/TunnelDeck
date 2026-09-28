# 发布包配套脚本

这两个脚本会随 Release 包分发，支持 Windows PowerShell 5.1。

| 脚本 | 用途 |
| --- | --- |
| `Check-Prerequisites.ps1` | 只读检查 Windows 11 x64、.NET、Windows App Runtime 和 Visual C++ 运行库；`-Json` 输出结构化结果，不安装软件或修改设置。 |
| `Smoke-Test.ps1` | 使用隔离数据和示例服务器验证启动、UI 编辑、配置升级、备份及重启；可指定旧版程序检查回退行为。 |

仓库中的源码位于本目录。`../Publish-Release.ps1` 将它们复制到发布包根目录，与 `TunnelDeck.exe` 和由 `docs/release.md` 生成的包内 README 放在一起。`../debug/Verify-Release.ps1` 仍从解压后的包根目录执行 `Smoke-Test.ps1`。

因此，包内直接运行的方式不变：

```powershell
.\Check-Prerequisites.ps1
.\Smoke-Test.ps1
```

`Smoke-Test.ps1` 的默认安装目录是脚本自身所在目录。若从源码目录调用，必须通过 `-InstallDirectory` 指向包含应用和配套脚本的完整发布目录。它需要可交互桌面；运行库检查不需要打开应用。

此次整理只迁移源码和更新复制路径，保留脚本原有内容。现有 smoke 仍包含 schema 2 的历史升级断言；针对当前 schema 3 发布前需先更新该断言，本次未将历史 smoke 视为当前版本已通过的验证。发布流程详见 [发布说明](../../docs/release.md)。
