# Import 自动预览与 Settings 窄屏布局

用户要求继续精简 Import 页面、将条目说明改为 DevWinUI SettingsExpander，并修复侧栏收缩时 Settings 控件向右溢出。延续 UI 重构期间仅做必要检查的约定。

## 改动

- `ImportPage` 将 File、格式 ComboBox、Browse 合并为一行，格式名称使用 JSON / SSH config。移除手动 Preview 按钮；文件选择、路径编辑和格式变化自动触发预览。
- `ImportViewModel` 为路径输入增加 350 ms 延迟、可取消请求及结果身份检查。预览期间路径仍可编辑；旧结果不能覆盖新选择。配置 revision 改变时自动刷新；选择同一路径、路径框 Enter、读取错误 Retry 可重新读取。Import 仍沿用原有可用条目、跳过确认和原子提交条件。
- 新增 `ImportPreviewEntry` / `ImportPreviewItem`，有说明的条目使用 SettingsExpander，Header 显示名称、端点和决策图标；无说明条目显示 SettingsCard。说明按行号和正文拆为两个只读 TextBox；不可用条目默认展开，条目 VM 保留展开状态。
- `ImportItem.AuthenticationNote` 标记原有认证说明，SSH parser 保留原 `Notes` 文本和所有解析行为，同时提供 footer 所需元数据；默认密钥 / 认证来源显示于 SettingsExpander.ItemsFooter。
- `SettingsPage` 明确滚动区域的水平 Stretch 和内容宽度上限，统一三个下拉框在窄屏中的宽度与对齐。密钥内层 SettingsCard 从 Left 改为 Vertical + Stretch，让长路径受可用宽度约束。缩放检查还发现控件会保留之前的 presenter 状态，因此尺寸变化时显式切换卡片的 Vertical / Right 视觉状态，避免返回宽屏或收缩侧栏后仍沿用旧对齐。
- 更新现有 Transfer 自动化入口以适配 ComboBox 和自动预览，不运行全套 UI 回归；同步设计规则与导入说明。

## 相关验证

- 原生 Visual Studio MSBuild Release x64 最终构建通过，0 警告、0 错误；使用独立输出目录，日志为 `artifacts/ui-refresh/iteration-3/build-final.log`。
- 现有 SSH parser / 认证默认值相关测试 49 项通过；未扩展到完整功能测试集。
- 首次隔离界面检查暴露 DevWinUI 的容器约束：SettingsExpander 会为每个条目应用 SettingsCard 样式，直接使用 Grid 作为 ItemTemplate 根节点导致异常。已将条目根节点改为 SettingsCard，保留内部双列 Grid；临时异常记录代码已移除。首次失败日志保留为 `import-exception.log`。
- 最终导入交互通过：路径输入自动预览、SettingsExpander 展开、跳过确认、格式变化使旧预览失效、重新预览重置跳过确认、一次实际导入保存私钥认证服务器。随后通过真实系统文件选择器选择有效 SSH config，预览和 Import 按钮自动就绪。证据为 `interaction.json`、`import-entry.json`、`import-final.png`。
- Settings 最终在 640 / 800 / 1200 DIP 窗口检查窄屏、收缩侧栏和恢复宽屏，使用长服务器名与长密钥路径；相关控件均处于页面水平边界内，卡片随宽度正确切换排列。证据为 `settings-layout.json` 和 `settings-*-final.png`。滚动容器内的字段分别在顶部 / 底部检查，不把正常的屏外字段当成溢出。
- 没有执行完整 UI、SSH、压力或系统显示设置回归。最终独立样例窗口保持在 Import 页面，进程 / 数据 / 可执行文件记录于 `review.json`；用户原有数据和先前未提交工作保留。本轮交付源代码与开发构建，未重新打发布 ZIP。
