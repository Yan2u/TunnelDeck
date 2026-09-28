# Release x64 收尾、性能与清理

用户已接受主要 UI 重构，要求补回有代表性的 UI 测试、配置 Release x64 打包流程、分析并优化内存，以及在测试完成后清理无用代码和中间产物。

## 工作约定

- 保留所有既有未提交开发成果；不重置、暂存或提交工作树。
- 使用项目指定的 Visual Studio Developer PowerShell；发布目标为 Release / x64 / win-x64。
- 性能测量使用独立样例数据和明确版本的可执行文件；对比相同场景，区分 Working Set、Private Bytes 与强制 GC 后的托管留存。
- UI 回归覆盖当前主要交互及近期修复，酌情减少已经多次验证的压力和系统显示设置循环。
- 全部验证完成后再删除工作区内的中间产物；保留最终发布包、简洁验收记录和必要证据，不清理用户应用数据或外部缓存。

## 初步检查

- 现有 LocalFolder 发布配置已指定 Release、x64、win-x64；发布脚本和文档仍以 M13 / 早期 UI 评测包命名，需要整理为当前发布入口。
- 解决方案中的 ProcessProbe 由终端启动测试引用，不能当作未使用项目删除。
- UI 工程没有使用显式引用的 CommunityToolkit 控件；另有旧 Microsoft.UI.Xaml 2.8.7 包及无须发布的多架构声明待验证清理。
- 清理前盘点：artifacts 约 1.014 GiB，.test-output 约 1.961 GiB；实际删除范围和释放空间以最后的清理记录为准。
- dotnet-gcdump 10.0.745401 安装在工作区 `.test-output/closeout-tools`，用于本轮隔离进程诊断。

## 实施的改动

- `src/TunnelDeck/TunnelDeck.csproj` 移除五个未使用的直接 CommunityToolkit 控件包及旧 Microsoft.UI.Xaml 2.8.7，更新锁文件；解决方案和 UI 项目统一为 x64。保留全部七个实际有用的项目，包括 Infrastructure 测试依赖的 ProcessProbe。
- Dashboard Advanced 表单与 Settings 密钥区域改为 `x:Load` 按需创建。Quick Forward 提交只读取当前实际加载的端口输入，保留基础 / Advanced 各自草稿。
- 新增 `ShellViewModel.Resources.cs`，复用资源行、服务器选项和四类有序列表；资源或顺序变化时重新投影，无关状态提交和导航复用已有对象。移除已失效的 TransferPreviewItem 决策 / Details 展示，Export 继续使用简化后的控件。
- `ReleaseX64.pubxml` 固定 Release / x64 / win-x64，替换 LocalFolder，取消 PDB、裁剪和单文件输出。`Publish-Release.ps1` 自动读取版本，检查全部 XAML 编译资源，输出 ZIP、SHA-256 与文件清单；`-Verify` 从 ZIP 执行独立验证。
- 新增 `Measure-Memory.ps1`、`Verify-Layouts.ps1`、`Verify-RuleDialog.ps1` 与 `Clean-GeneratedArtifacts.ps1`。发布、测试、性能和清理文档均更新。

## 回归中发现的真实崩溃

Profile 测试两次在规则默认服务器检查时超时，实际是进程退出。WER 小型转储显示 stowed exception `0xc000027b`，内部为 `0x80004003`，不能据此归因于 RivaTuner。隔离进程 16716 的完整转储在首次空引用处明确显示：

```text
RuleEditorDialog.OnOpened
  → RuleEditorView.FocusEditor
  → Model == null
```

`Opened` 可早于弹窗内容的 Loading / 编译绑定初始化。现在 `RuleEditorDialog` 构造函数在 `InitializeComponent()` 后立即 `Bindings.Update()`，确保聚焦前已提供 Model 和服务器集合。没有压制异常、加入任意延时或修改用户图形叠加层。临时 App.UnhandledException 记录已移除。

Native-DebuggerSupport 改为不在正常 CLR 首次 AV 上暂停，仅捕获最终 stowed exception；Profiles 验证可选附加调试器。诊断时暂停的隔离进程已核对身份后结束，构建锁定已解除，最终重新构建零警告、零错误。

另一个失败是旧 Shell 断言要求 Dashboard 固定不随菜单滚动。当前 UI 已改为原生 NavigationViewItem；测试改为检查固定 Settings / 状态页脚，以及滚回顶部后的 Dashboard 可达性，不回退用户已接受的设计。

## 验证与发布

- 锁定依赖的 Release x64 解决方案构建通过；最终 UI 修复构建零警告、零错误。
- Core 428 + Infrastructure 148 = 576 项通过，无失败、无跳过。
- Shell、Transfer、AuthenticationDefaults、真实 Forwards、完整 Profiles、右键 / Shift+F10、主题 / 480–1200 DIP 适配，以及一次原生拖动与顺序保存通过。
- 修复后规则对话框 15 次连续校验 / 应用 / 取消 / 保存通过，最终包另验证 3 次。
- 最终包直接验证 Settings 收缩侧栏下 1198 / 1200 / 1360 / 再次 1200 DIP 的留白；Dashboard 模式、控件顺序、Advanced 草稿及 500 DIP 换行通过。
- ZIP 从含中文 / 空格的路径启动，Windows PowerShell 5.1 smoke 验证 UI 编辑、schema 1 → 2 及原备份、重启不重写配置、无自动连接。84 个文件散列和 23 个 XBF 完整。
- 交付 `artifacts/release/TunnelDeck-1.0.0-win-x64.zip`，34,099,724 bytes；SHA-256：`766cd0232b29c3d71f0f4585949a1a5510a02fda8e62d7dbaf23470afc703153`。未签名、依赖共享运行库、未上传。

## 性能结论

250 Server + 250 Profile，预热后导航 12 轮。基线 / 最终包的最后一次 GC 后 Private Bytes 为 248.17 / 254.62 MiB，Working Set 为 301.95 / 299.03 MiB；另一轮优化构建为 242.57 MiB 私有内存。不能选较低的一次宣称整体 RAM 明显下降。

实际减少的是 ResourceRow 750 → 540、NumberBox 8 → 4、TextBox 9 → 1；最终托管留存约 4.74 MiB，Dashboard / Settings 各两个实例，未随本轮导航持续累积。没有发现需要更改计时器生命周期的证据，也没有添加应用内 GC / 工作集裁剪技巧。

最终包另跑真实 10 Local + 10 Remote：空闲 / 20 条运行的 Private Bytes 为 143.05 / 202.01 MiB，Working Set 为 199.50 / 271.86 MiB；60 秒空闲平均约 0.0011% 整机 CPU，20 个 payload 及所有监听释放通过，正常关闭约 292 ms。不是干净机器或长期压力结论。

## 清理与保留

关闭全部本轮测试实例，清除一个两天前已停止的、标签和 manifest 匹配的 SSH 夹具容器。清理脚本先检查全部绝对路径、重解析点、跟踪文件与占用进程，再删除工作区内的 `.test-output`、旧 artifacts、发布 smoke 数据和七个项目的 bin / obj。保留最终 release 目录、源码、docs/evidence、Visual Studio 偏好及用户应用 / SSH 数据；不执行 git clean、reset、暂存或提交。

实际删除 8,793 个文件，逻辑大小合计 4,401,408,887 bytes，约 4.10 GiB；逐项目清单见 [清理记录](../../docs/evidence/2026-09-23-release-cleanup.json)。已打开的 Visual Studio 随后重建少量 Debug 设计期文件，不为追求空目录而关闭 IDE。验收摘要见 [收尾证据](../../docs/evidence/2026-09-23-release-closeout.json)，包含原始导航采样的 [性能证据](../../docs/evidence/2026-09-23-release-performance.json) 保留在仓库。旧截图、日志、工作区转储、临时工具与中间发布包不再保留。

用户允许选择性恢复回归，本轮未重复全套 SSH 87 项、长期压力、系统对比 / 字号设置、多显示器或 Narrator 实际语音。干净 Windows 依赖安装、签名和上述显示 / 朗读范围仍按发布文档列为边界。
