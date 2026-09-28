# M13：渲染排查与资源列表视口

2026-09-21，M12 GUI H 已由用户确认，开始 M13。本文记录阶段成果，不代表 M13 最终验收完成。

## 渲染排查

M12 失败进程的 WER 模块列表包含 `RTSSHooks64.dll`。微软 WindowsAppSDK issue 2468 记录过 RTSS 对 WinUI 的兼容问题，但其历史异常并不直接证明本次异常的内部根因。

用户主动关闭 RivaTuner 后，**未修改产品代码的原 TunnelDeck.exe** 完整 Transfer 原生回归 11 组通过，模块列表中不再有 RTSS；随后连续 20 轮 500×580 / 1200×760 缩放通过。证据位于 `artifacts/m13-review/renderer/rtss-closed` 与 `resize-rtss-closed`。但后续 `native-regression/transfer` 在没有 RTSS 模块的 PID 49444 中再次复现相同异常（WER 352e50a4-8259-490a-ab44-a719feb66140）。因此撤回先前“关闭 RTSS 是已验证规避条件”的判断；后续原始转储还原出焦点重入异常，已调整表单 / 预览结构；见 [焦点修正](2026-09-21-m13-import-focus-reentrancy.md)。

没有修改 RTSS 全局配置或添加异常压制。较早的同字节别名对照测试停在文件选择器，未用于推断原因；该诊断别名已移除。`Run.ps1` / Transfer / Resize 脚本新增可指定可执行文件的入口，后续用于测试发布目录。

## 1000 + 1000 资源规模

新增 `Scale-TestSupport.ps1`、`Verify-ResourceScale.ps1`，使用 1000 个 Server、1000 个各含一条规则的 Profile、每类 20 个 Pinned，均为 .example 数据。

原 All 页在外部 ScrollViewer / StackPanel 内禁用了两个 ListView 的滚动，给出无限高度。两次基线在遍历 Server 行的 UIA 子树时失败；一次现场采样为 590,000,128 bytes 私有内存、35.875 CPU 秒且暂时不响应。搜索 Core 本身的 P95 约 52 ms，未对已达标搜索引入新框架。

`ResourceListView.xaml` 改为两个有界原生列表：Pinned 最多占资源视口的 40%，只有 Pinned 时可使用全部；Other 使用剩余区域，分别滚动。`ResourceListView.xaml.cs` 根据真实视口大小设置上限；`ResourceListViewModel.HasOther` 防止空列表遮住空态按钮。

同规模验证通过：每页约 53 个已实例化行按钮，最后一项可滚动到达、Sidebar 保持上限、搜索正确导航且不写配置。Server / Profile 页私有内存约 164 / 186 MiB。原始与新结果位于 `resource-scale-before-detail` / `resource-scale-after`；原基线的内存采样来自 UIA 遍历期间，不将其与新静态采样冒充严格峰值对比。

## DPI 与处理耗时

清单显式声明 PerMonitorV2。真实 Windows 缩放 100% / 150% / 200%（96 / 144 / 192 DPI）在同一进程中验证 Dashboard、列表、Settings 与窄窗 Import，全部通过并恢复原 125%；只有一块物理显示器，不能视为多显示器切换验收。

`UiPerformanceProbe` 仅在显式隔离实例且提供 `TUNNELDECK_UI_TIMINGS` 时记录操作名与耗时；不记录资源名称、端点或凭据。`Measure-UiPerformance.ps1` 使用真实键盘事件触发搜索，排除 UIA / IPC 与网络等待：Navigation 25 个样本 P95 31.98 ms，Search 47 个样本 P95 38.41 ms。报告 `ui-performance-native-input/handlers.json` 保留首次初始化的最大值，不以 P95 声称每次调用都低于 100 ms。

高对比度页面检查通过，关闭状态与 Flags 已恢复；Windows 将首次读取时为空的备用方案名补全为“高对比黑色”，原始结果及随后检查保留该差异。文字放大使用真正的 Windows RangeValuePattern，已观察到正文放大及标题 47 → 61 px；WinUI 的非线性缩放不应按所有字体一律放大两倍判断，完整布局检查仍在继续。

## 后续

663 项 Core / Infrastructure / 真实 SSH 全量测试已通过。发布、独立旧版本升级 / 回退、60 秒空闲和 20 条真实转发性能、键盘与原生回归已有通过证据；最终结果及仍待验收的环境项目见 `docs/m13-validation.md`。M10–M13 工作区改动尚未提交。
