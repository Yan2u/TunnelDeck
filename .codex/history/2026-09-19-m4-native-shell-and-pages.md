# M4：原生 Shell、六页与导航

日期：2026-09-19。继续 M3 工作区，保留 unpackaged 直接 exe 及 Local-only 自动找端口范围。没有提交 Git，也没有修改 `.codex/prompt/260919.md` 的用户内容。

## 目的与实现

用可运行的六类页面替换 FoundationPage，交付 GUI 评测 A 所需的原生布局与交互。M4 实现和自动化验证已完成；视觉验收仍待用户确认。

- `Core/Navigation`：带 Guid 的路由、唯一 history、相同路由去重、Back、资源失效清理、条件替换。保存完成后不会覆盖用户已经选择的其他路由。
- `Core/Presentation`：名称 / endpoint / 路由展示与纯全局搜索；名称、host、username 命中及稳定排序，重名结果保留独立 ID 和描述。
- `TunnelDeck/MainWindow*.cs/xaml`：Tall TitleBar、原生 Back、全局 AutoSuggestBox、窄窗搜索 Flyout、Ctrl+K / Alt+Left；固定 Dashboard 与 Settings / 状态，中间资源统一滚动。沿用原生插槽，未改 ControlTemplate。
- `TunnelDeck/Pages`、`Controls/ResourceListView`、`ViewModels`：Dashboard、Profiles、ProfileDetail、Servers、ServerDetail、Settings。使用原生 CommandBar、ListView、ComboBox、NumberBox、RadioButtons、Flyout 和系统样式。
- 复用 M3 的基本 Server 字段 / Profile 名称保存，Pin / Unpin、Pinned 菜单移动、Sidebar / Forwarding 设置。草稿验证和保存失败保留旧配置；凭据 / 连接 / Rule 编辑 / 网络操作仍按 M5–M8 接入。
- `TunnelDeck/Design`：Sample / Stress / Empty，包含重名、长中英文名称、长 IPv6、28 项资源与超限 Pinned、运行 / 重连 / 端口调整的显示样本。只写隔离数据，不建立连接。
- `scripts/Verify-Shell.ps1`：六页、保存 / Pin / 菜单移动、搜索 / Back、主题、空态新增、900 / 640 / 500 epx 与重启持久化的可复跑 GUI 验证。

## 实窗修正

- 避免窗口首次尚未布局时按默认 Minimal 状态提前收起宽窗侧栏。
- 修正不被当前 SDK 接受的 `Icon="Down"`，改用 FontIcon。
- 资源行用真实 Button 提供键盘 / UIA 打开操作，并添加稳定 AutomationId 与完整可访问名称。
- 保存期间只更新可用状态，不为忙碌切换重建列表，避免破坏用户刚打开的菜单。
- 状态与 Settings 一起放进原生 PaneFooter，保持顺序和固定位置；不把 TextBlock 塞进将 Content 当字符串的 NavigationViewItemHeader 模板。
- 收起空错误块 / 已关闭提示的占位，修正长标题原生 overflow 和窄窗表单对齐。
- GUI 脚本显式设置 DPI 上下文，避免 PowerShell 坐标虚拟化重复缩放。采用窗口渲染截图；键盘输入确认前台属于目标 App 或其 popup。

## 验证

- Release x64 锁定还原构建：0 warnings / 0 errors。
- `Test.ps1`：**229 通过，0 失败，0 跳过**，Core 152（新增 15）、Infrastructure 77。
- `Verify-Shell.ps1`：全部九组检查通过，含保存不抢导航、Local-only UI、搜索 / Back、固定侧栏、Pin / 顺序、宽窄布局、空态新增和重启。
- `Verify-Window.ps1`：固定区域、标题栏输入、pane toggle、短窗、重复关闭通过；关闭约 997 ms（包含 750 ms probe），期间窗口响应。
- `Verify-Storage.ps1`：未来版本保护、备份 / 双损坏提示、外部修复后 Retry、正常加载 / 关闭不改写配置全部通过。
- 额外检查 640 epx 长标题 Profile Detail，Save / Cancel 进入原生 overflow 后仍可用。
- Light / Dark 与目标窗口截图已核对。High Contrast 未切换系统设置，GUI 评测 A 待确认。

当前证据见 `docs/evidence/2026-09-19-m4.json`，页面契约见 `docs/shell-and-navigation.md`。M4 没有修改 SSH 传输，未重复运行真实 sshd；此前 M2 的 18 项 SSH 证据继续保留。

下一步先收集 GUI A 反馈，确认后继续 M5 的凭据、host key 确认与真实连接管理。
