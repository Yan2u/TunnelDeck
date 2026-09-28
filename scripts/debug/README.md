# 调试与验证脚本

从仓库根目录调用，使用 PowerShell 7。日常构建、运行、单元测试和发布入口见 [上级目录](../README.md)。

需要真实 SSH 的验证脚本共用 [fixtures/](../fixtures/README.md) 中的启动、停止脚本及容器服务。

## 按需使用

| 脚本 / 类别 | 用途 |
| --- | --- |
| `Verify-NativeRegression.ps1` | 顺序运行原生回归，可用 `-Suites` 选择模块。 |
| `Verify-Release.ps1` | 解压、核对清单和执行发布包 smoke；由 `../Publish-Release.ps1 -Verify` 调用。 |
| `Verify-ImportFocus.ps1`、`Verify-ImportResize.ps1`、`Verify-ImportScale.ps1` | 历史导入焦点、缩窗及规模问题的专项复现。 |
| `Verify-ResourceScale.ps1` | 大规模资源列表检查。 |
| `Verify-Accessibility.ps1`、`Verify-DisplayScaling.ps1`、`Verify-TextScaling.ps1`、`Verify-HighContrast.ps1` | 无障碍、DPI、文字缩放和高对比度检查。 |
| `Measure-Application.ps1` | 启动、真实转发和应用生命周期测量，使用 SSH 夹具。 |
| `Measure-UiPerformance.ps1` | UI 时序测量。 |
| `Measure-Memory.ps1` | 内存采样，可指定诊断工具采集 GC dump。 |
| `Show-UiReview.ps1`、`Show-FinalReview.ps1` | 打开带样例资源和历史的隔离评测窗口。 |
| `*-TestSupport.ps1`、`Deletion-ForwardingChecks.ps1`、`Lifecycle-ForwardingChecks.ps1`、`Lifecycle-InstanceChecks.ps1` | 供入口脚本 dot-source 的辅助实现，依赖调用方上下文。 |
| `Native-DebuggerSupport.ps1` | 为指定的隔离测试进程捕获原生异常。 |
| `Set-NativeRangeValue.ps1` | 按明确窗口句柄和 AutomationId 操作滑块的手动诊断工具。 |

GUI 脚本会操作真实窗口，需顺序运行。显示与无障碍脚本还涉及系统设置。按当前改动选择所需场景即可；脚本保留不等于每次都需要全部运行。

部分脚本保留旧版本断言，尤其是界面重构、多语言和 schema 3 之前的场景。运行前检查其假设；历史通过记录不能代替当前版本结果。

## 功能与评测入口

以下命令从项目 README 迁入，路径已更新：

```powershell
# 检查真实顶层窗口、侧栏滚动、标题栏输入与异步关闭
./scripts/debug/Verify-Window.ps1

# 使用隔离数据检查未知版本、备份恢复和错误重试 UI
./scripts/debug/Verify-Storage.ps1

# 六类页面、保存 / Pin、搜索 / Back、主题和窄窗交互
./scripts/debug/Verify-Shell.ps1

# 隔离真实 SSH：凭据表单、指纹确认、重启、取消和关闭
./scripts/debug/Verify-Connections.ps1 -NoFixtureBuild

# 隔离真实 SSH：Quick Forward、实际 payload、端口调整、重试和退出清理
./scripts/debug/Verify-Forwards.ps1 -NoFixtureBuild

# 24 行活动列表、独立滚动、键盘详情、宽窄与 Light / Dark
./scripts/debug/Verify-Dashboard.ps1

# Profile 草稿、跨服务器启动、部分失败与重试、真实 payload 和退出
./scripts/debug/Verify-Profiles.ps1 -NoFixtureBuild

# 单个 / 全量删除、确认取消、保存失败重试、真实监听释放与关闭
./scripts/debug/Verify-Deletions.ps1 -NoFixtureBuild

# Pinned 鼠标拖排、键盘等效操作、写入失败回退、搜索和重启
./scripts/debug/Verify-Resources.ps1

# 单实例激活、真实断线重连展示、退避期间 Stop、保存失败 Retry / 不保存退出、重复关闭
./scripts/debug/Verify-Lifecycle.ps1 -NoFixtureBuild

# 导入 / 导出预览、系统文件选择器、原子失败重试、SSH config 警告和终端入口
./scripts/debug/Verify-Transfer.ps1

# 打开带 JSON / SSH config 样例的隔离 M11 GUI 评测窗口
./scripts/debug/Show-TransferReview.ps1

# Recent / Statistics / Settings、保存失败重试、重启与宽松 SSH config 导入
./scripts/debug/Verify-Activity.ps1

# 打开带 200 条样例活动的隔离 M12 GUI H 评测窗口
./scripts/debug/Show-ActivityReview.ps1

# 默认认证、系统密钥选择器、SSH config 显式优先级与重启预填
./scripts/debug/Verify-AuthenticationDefaults.ps1

# 当前 UI 的右键菜单、主题和宽窄布局
./scripts/debug/Verify-UiRefresh.ps1

# 收缩侧栏下的 Settings 宽度阈值、Quick Forward 排列与 Advanced 草稿
./scripts/debug/Verify-Layouts.ps1

# 规则对话框反复打开、校验、取消与草稿保存
./scripts/debug/Verify-RuleDialog.ps1

# 打开隔离的默认认证设置评测窗口
./scripts/debug/Show-AuthenticationReview.ps1
```

性能口径与命令见 [性能记录](../../docs/performance.md)，发布流程见 [发布说明](../../docs/release.md)。
