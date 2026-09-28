# 修复 Settings 跨最大宽度阈值时的偏移与溢出

用户再次反馈：侧栏收缩后，窄窗口正常，拉宽到一定程度时整个设置页向右偏移，右侧控件被裁剪。本轮只处理这一布局问题。

## 根因与复现

- 使用上一轮构建，在同一隔离样例中复现“1200 DIP 窗口、侧栏收缩”组合；展开侧栏时不触发。
- 原结构让带 `MaxWidth=1120` 的 Stretch StackPanel 直接成为 ScrollViewer 的内容。视口刚超过最大宽度时，滚动容器按较小的 DesiredSize 居中，但子元素实际仍按最大宽度绘制。
- 1200 DIP 窗口下，实际视口宽 1121.14 DIP，StackPanel 的 DesiredSize 为 902.86 DIP，实际宽 1120 DIP；左侧错误留白为 109 DIP，右侧超出约 107.86 DIP。1198 DIP 窗口尚未超过阈值，不发生偏移。
- 上轮较宽窗口检查时侧栏自动展开，未覆盖这个组合；UIA 返回的控件矩形还可能已经被裁剪，不能独立证明完整控件没有溢出。本次使用临时进程内布局记录，读取 ActualWidth 和 TransformToVisual 得到未裁剪边界。

## 修复

- `SettingsPage.xaml` 在 ScrollViewer 与有最大宽度的内容之间增加 Stretch Grid。Grid 承接完整视口，内部 StackPanel 保留原有 1120 DIP 上限、间距与卡片样式。
- `SettingsPage.xaml.cs` 不再在 SizeChanged 中反复设置内容 MaxWidth；只读取固定上限和当前可用宽度，计算卡片的横向 / 纵向布局。
- 保留上一轮的下拉框与 SettingsCard 视觉状态切换，未改变设置保存、认证默认值等功能。

## 验证

- 侧栏全程收缩，分别检查 1198、1200、1360、800、再次 1200 DIP。内容未裁剪边界均位于视口内，左右留白相等；1200 DIP 时两侧均为约 0.57 DIP，1360 DIP 时两侧均为约 80.57 DIP。
- 证据在 `artifacts/settings-viewport-fix`：`before-layout.jsonl`、`after-layout.jsonl`、`layout-checks.json` 及修复前后截图。
- 临时布局记录代码已移除；最终 Release x64 构建与独立窗口启动结果见 `build-final.log`、`review.json`、`final-collapsed.png`。
- 没有运行单元测试全集、SSH、压力或其他页面回归；没有发布 ZIP 或提交代码。保留所有已有未提交工作。
