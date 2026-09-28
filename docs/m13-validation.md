# M13 验证与本地评测包

2026-09-21 开始，2026-09-22 更新。M0–M12 / GUI A–H 已由用户确认。M13 已完成下列修正与本机验证，**仍待最终 GUI 及尚无完整证据的环境项目验收**。M10–M13 工作区改动未提交。

| 工作项 | 当前状态 | 证据 / 交付 |
| --- | --- | --- |
| 导入页缩窗异常 | 已移除触发结构并回归 | 原始转储确认 GettingFocus 中同步重置父 ListView；完整 Transfer 含 20 次缩窗、20 次格式 / 焦点循环通过，添加行语义后再验证 5 次 |
| 非 GUI 全量与真实 SSH | 663 项通过，0 跳过 | Core 428、Infrastructure 148、真实 SSH 87 |
| Light / Dark / High Contrast、DPI | 通过本机范围 | 新导入预览在真实 100/150/200% DPI 与高对比下通过；恢复 125%，高对比已关闭、Flags 126 恢复 |
| 200% 系统文字大小 | 未通过自动化验证 | Windows 设置滑块变化后未实际应用全局字号；原 100% 与注册表值不存在的状态已恢复，不标为通过 |
| 键盘、名称和焦点 | 本机与包内程序通过 | 新建 Server / Profile 焦点、Tab / Shift+Tab、Enter / Esc、字段错误名称、只读行语义、导入底部操作；Narrator 实际语音未测 |
| 1000 Server / 1000 Profile、搜索与虚拟化 | 修正无限高度后通过 | 每页约 53 个行按钮；Core 搜索 P95 约 53 ms，UI 处理 P95 导航 32 ms / 搜索 38 ms |
| 1000 条 SSH 导入预览 | 通过 | 顶部 / 底部 / 顶部仅实例化 9 / 11 / 9 个可访问行；首尾可达，窄窗按钮可见，取消不写盘 |
| 启动、60 秒空闲、20 隧道与 100 次循环 | 通过当前测量范围 | 发布程序启动 623–812 ms；空闲单核 CPU 0.18%；20 条真实转发和清理通过，100 次循环来自测试宿主，详见 performance.md |
| 最终 Release x64 ZIP | 已生成并通过开发机 smoke | 文件哈希、真实 UI 升级、原备份、重启、旧版只读保护及独立备份回退通过；多文件、共享运行库、未签名 |
| 干净 Windows 11 安装 / 升级 / 回退 | 待确认环境 | 没有干净环境时如实保留未验证项 |
| 最终人工 GUI 评测 | 待用户确认 | 复验导入缩窗、Pinned / Other 滚动、键盘焦点、主题和整体观感 |

13 套原生回归 Resources、Shell、Window、Dashboard、Activity、AuthenticationDefaults、Connections、Forwards、Profiles、Deletions、Lifecycle、Storage、Transfer 已通过。Lifecycle 夹具改为当前 schema 2，避免将合法升级提示误判为异常。原始证据位于 `artifacts/m13-review` 的 `native-regression`、`native-remaining`、`lifecycle-storage`、`transfer-after-focus-fix`、`import-focus-after`、`import-focus-accessible`、`import-scale-verified`、`display-scaling-import-fixed`、`high-contrast-import-fixed`、`packaged-accessibility` 和 `rc-final-smoke`；历史失败结果保留。

## 原始异常与修正

没有 RTSS 模块的 PID 49444 仍曾崩溃。原始 WER minidump 的真实托管异常消息是 “Focus cannot be moved while getting or losing focus.”，调用链为 RadioButtons.SelectedIndex → SourceIndex → Invalidate → RefreshPlan → ListView.ItemsSource。不能仅凭 `0x802b0020` 的系统符号名认定显卡 / surface 故障。

现在有界 ScrollViewer 内的表单与 ItemsRepeater 预览互为独立内容，底部动作固定；只读行有命名的 ListItem automation peer。没有吞掉异常、限制最小窗体或修改叠加层配置。新增焦点用例在旧版本也曾通过，因此不将某次绿色运行冒充确定性前后对照；定位依据是原始异常链，后续用例验证修正后的实际行为。详见 [修正历史](../.codex/history/2026-09-21-m13-import-focus-reentrancy.md)。

## 尚需验收

- **200% 系统文字**：最终自动化中滑块到了 200%，但注册表有效值与实际应用字号没有变化；应用标题仍为 47 px。早期一次放大观察不能替代完整流程。原文字设置已恢复，证据为 `text-scaling-native-button/result.json`。
- **Narrator 实际语音 / 错误播报**：字段错误在 XAML 中设置 Polite；当前 winapp 对 LiveSetting 读数为 null，不能算运行时播报已验证。
- **干净 Windows 11 安装**：本机已有共享运行库，Hyper-V / Sandbox 未启用；开发机启动与升级 smoke 不替代干净机器安装。
- **多显示器 DPI**：只有一块物理显示器，系统缩放检查不替代跨显示器移动。
- **用户最终 GUI**：复验导入预览缩窗、确认按钮、Pinned / Other 滚动、新建表单焦点与整体观感。

## 本地候选包

`artifacts/m13-review/rc-final/TunnelDeck-m13-win-x64.zip`：31,584,908 bytes；完整解压 88,094,994 bytes。多文件、共享运行库、未签名，没有外部发布。

SHA-256：`03183beb75196d75302a8933c853e3bfd183bb04c01ce02574ddcdc730dd9c80`

运行库和数据说明见 [release.md](release.md)，性能口径与 M0 对比见 [performance.md](performance.md)。`Show-FinalReview.ps1 -ExecutablePath <解压后的 TunnelDeck.exe>` 使用独立数据 / 凭据命名空间、.example 样例与演示历史，不自动连接服务器。正常用户配置未被本轮测试修改。

原始汇总见 [M13 证据索引](evidence/2026-09-22-m13.json)。
