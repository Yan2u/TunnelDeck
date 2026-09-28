# 脚本目录

在仓库根目录使用 PowerShell 7。一级目录保留日常入口：

| 脚本 | 用途 |
| --- | --- |
| `Build.ps1` | 构建解决方案；默认 Release x64，支持锁定还原。 |
| `Run.ps1` | 启动已构建应用，也支持隔离数据、布局预览和窗口 probe。 |
| `Test.ps1` | Core / Infrastructure 测试；`-Ssh` 额外运行真实 SSH 测试。 |
| `Publish-Release.ps1` | 打包 Release x64；`-Verify` 调用 `debug/Verify-Release.ps1` 验证解压后的发布包。 |
| `Clean-GeneratedArtifacts.ps1` | 按已有保护规则清理生成文件，支持 `-WhatIf`。 |
| `Enter-DevelopmentShell.ps1` | 构建、测试、发布及部分调试脚本共用的 Visual Studio 环境初始化；不是普通启动的前提。 |

## 开发调试工具

其余 47 个脚本保留在 [debug/](debug/README.md)：

| 类型 | 数量 | 保留原因 |
| --- | ---: | --- |
| `Verify-*.ps1` | 26 | 功能、布局、显示设置及发布包验证，包含历史问题复现入口。 |
| `Show-*.ps1` | 5 | 创建隔离样例并打开人工评测窗口。 |
| `Measure-*.ps1` | 3 | 应用、UI 耗时和内存分析。 |
| `*-TestSupport.ps1`、`*-Checks.ps1`、调试辅助工具 | 13 | 被验证脚本加载的公共实现，以及手动诊断工具。 |

这些工具不参与应用运行，也不是普通构建所必需的，但并非无用文件。辅助脚本存在相互依赖，发布脚本的 `-Verify` 也依赖其中的验证入口，因此本次集中保存，未删除。

旧的 `scripts/Verify-*.ps1`、`Show-*.ps1`、`Measure-*.ps1` 命令现在需要加上 `debug/`。同名脚本和参数保留，测试输出仍位于仓库根目录的 `.test-output/`、`artifacts/`。历史 evidence 和 history 中的旧路径记录保留原貌。

本次仅验证脚本语法与路径依赖。部分里程碑脚本仍依赖当时的 UI 文案、布局或 schema 断言，例如 `Verify-Storage.ps1` 仍将 schema 3 当作未来版本。重跑这类脚本前需先适配当前版本；目录整理不代表全套回归已通过。

## SSH 测试夹具

[fixtures/](fixtures/README.md) 集中保存 `Start-SshFixture.ps1`、`Stop-SshFixture.ps1` 和配套 `sshd/` Dockerfile、Python 控制服务。`Test.ps1 -Ssh` 及需要真实连接的调试脚本共用这套夹具。普通构建和不带 `-Ssh` 的测试不需要启动它。

`tests/` 中的 C# 文件属于三个 .NET 测试项目；`test-infra/TunnelDeck.ProcessProbe/` 是被集成测试构建和调用的辅助程序。它们保留在原来的项目目录，不作为脚本移入此处。

## 发布包配套脚本

[packaging/](packaging/README.md) 保存 `Check-Prerequisites.ps1` 和 `Smoke-Test.ps1`。它们随发布包一起分发，由 `Publish-Release.ps1` 复制到包根目录；包内的运行命令保持不变。前者只读检查运行库，后者在隔离数据目录中进行发布验证。
