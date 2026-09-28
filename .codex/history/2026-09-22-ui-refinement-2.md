# UI / UX 第二轮：设置分区、行对齐、规则对话框与收缩导航

响应用户对第一轮视觉稿的反馈，保持 SSH、认证、导入、Profile 保存及运行行为不变。保留此前全部未提交工作。

## 改动

- `Pages/SettingsPage.xaml`：移除 SettingsGroup，用独立 TextBlock 标题和 SettingsCard / SettingsExpander；分区间 32 DIP、标题与卡片间 12 DIP、同区卡片间 4 DIP，保留现有窄窗纵向布局。
- `Controls/TunnelListItem` 与 `StatusIndicator`：状态图标 / 标签和三个操作按钮共享垂直居中的区域；窄行一起移到下方。宽行不再为不存在的第二行保留间距。
- `Controls/RuleEditorDialog`：新增统一样式的 ContentDialog，复用原 RuleEditorView 与 ProfileEditorModel 校验。取消 / Escape 放弃当前规则编辑，应用仅更新页面草稿，保存仍使用原 Save。正文随窗口高度滚动，操作区固定；无效输入保持打开并聚焦错误字段。
- `Pages/ProfileDetailPage.Rules.cs` 与 `ViewModels/ShellViewModel.RuleEditing.cs`：新增、编辑均通过已有 DialogService 队列显示表单，退出页面 / 关闭窗口取消浮层；关闭后恢复列表焦点。删除原页面底部追加的规则表单。
- `MainWindow`：Dashboard 从会被折叠隐藏的 PaneHeader 移入原生菜单；Settings 从普通按钮改为原生 FooterMenuItems 项；收缩宽度设为 64 DIP。底部状态在展开时显示完整文字，收缩时显示 32 DIP 圆形数字，tooltip / 可访问名称保留完整活动数量。
- `docs/ui-design-system.md`：同步本轮布局和交互规则。

## 验证策略

本轮遵照用户要求大幅精简验证：仅 Release 发布构建、隔离样例启动，以及上述四处的少量针对性交互。没有执行完整 UI 回归、SSH 连接回归、压力测试或系统显示设置测试。最终结果记录于 `docs/ui-refresh-validation.md`。

发布构建通过。规则校验、取消不写盘、Apply 更新草稿 / Save 持久化、再次编辑回填通过；隧道状态及三个按钮的垂直中心偏差均为 0 px。收缩侧栏图标完整，徽标与导航项水平中心偏差为 0 px。截图和轻量检查数据位于 `artifacts/ui-refresh/iteration-2`。

最终包：`artifacts/ui-refresh/release-2-final/TunnelDeck-ui-refresh-2-win-x64.zip`；SHA-256 `ca3913fa5aa360d0b952e8f17b579a032a990cce19b7696cbdbbb5eb4b08331c`。最后一次发布仅补齐徽标容器宽度，复核启动与侧栏，未重复无关检查。`Show-UiReview.ps1` 已打开最终版全新隔离样例窗口；之前两个中间版本的测试窗口已正常关闭。
