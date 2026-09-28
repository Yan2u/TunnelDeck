# TunnelDeck UI design · iterations 1–3

2026-09-22。用户已确认当前功能基本可用，授权在功能保持不变的前提下持续重构 UI / UX。此设计替代旧 UI spec 中与本轮明确要求冲突的呈现方式，不改变连接、认证、导入、删除、持久化等行为约定。

## Foundation

- 面向熟悉 SSH / 网络的用户。主界面显示可操作内容，解释移入有名称、可用键盘打开的帮助 / 详情 Flyout；错误、端口调整和破坏性操作影响仍当场显示。
- 主窗口保留 Mica。内容使用 WinUI Layer / Card 系统画刷。对话框统一应用 `DefaultContentDialogStyle` 的圆角、按钮区、主题和遮罩；Mica 是 Window backdrop，不给 ContentDialog 伪造 Acrylic 材质或另开窗口。
- 使用系统 `Title`、`Subtitle`、`BodyStrong`、`Body`、`Caption` 字体层级；主文字、辅助文字、状态分别用语义 ThemeResource。只有端点使用等宽字体。禁止颜色字面量、装饰渐变和叠加卡片边框。
- 宽屏页面留白 24–32 DIP，窄屏 16 DIP；普通组间 24，设置分区之间 32、标题与卡片之间 12、同区卡片之间 4，卡片内 16，相关元素 8–12。图标采用 Segoe Fluent / Symbol，图标按钮同时提供 ToolTip 与 AutomationProperties.Name。
- 页面标题旁保留主操作，低频操作使用原生 CommandBar overflow / MenuFlyout。表单保留原有验证、输入焦点和保存行为。

## Collection anatomy

| 集合 | 数据字段 / 意义 | 首层内容 | 二级内容 | 容器与交互 |
| --- | --- | --- | --- | --- |
| Servers | Id, Name, Host, Port, Username, Authentication, pin order | 服务器图标、名称、user@host:port；认证图标 / 简短方式；Pin / More | 完整长端点 tooltip；Open、Pin/Unpin、Move up/down、Remove 菜单 | 有界 GridView + ItemsWrapGrid，随宽度 1–3 列；保持虚拟化与 Pinned 拖放 |
| Profiles | Id, Name, Rules, pin order | 配置图标、名称、规则数量；Pin / More | Open、Pin/Unpin、Move up/down、Remove | 与 Servers 同一种卡片及菜单语义 |
| Active tunnels | Requested/Actual endpoints, server snapshot, State, LastError | 端口路线为主、server 为辅；图标 + 状态与 Stop/Retry、Details、More 在同一区域垂直居中 | 完整端点、时长、source、连接请求历史 | 有界 ListView；静态行背景；子按钮独立 hover/focus。窄行将状态与操作一起移到下方，端口调整和失败信息留在首层 |
| Recent activity | Source snapshot, Outcome, Requested/Actual, OccurredAt | 名称、结果图标、简短路线、相对时间 | 完整时间、请求 / 实际路线、重命名 / 删除注释、错误 | 有界 ListView；单一详情按钮作为每行的交互表面 |
| Import preview | Name, Description, Decision, Notes, AuthenticationNote | 决策图标、名称、端点；省略独占一行的 New server | SettingsExpander 内按“行号 / 说明”双列展示只读 TextBox；认证来源放 ItemsFooter；不可用条目默认展开 | 有界 ScrollViewer + 虚拟化 ItemsRepeater；无说明时使用 SettingsCard；展开状态由条目 VM 保存；输入区独立，操作区固定 |
| Export preview | Name, Description | 图标、名称、端点 / 规则数量 | 保持原有导出范围和说明 | 有界 ScrollViewer + 虚拟化 ItemsRepeater |
| Detail rule lists | Name, server, endpoints, enabled/runtime state | 名称与端点；启用状态及原有操作 | 新增 / 编辑共用原生 ContentDialog | 统一列表间距和安静容器；对话框仅修改当前 Profile 草稿，仍需页面 Save 保存 |

## Interaction rules

资源卡片和含动作的隧道行本身不渲染 PointerOver 填充；可点击的名称区和动作按钮各自使用原生样式。保留平台键盘焦点框，不靠隐藏所有焦点或自制 ControlTemplate 消除 hover 冲突。所有动作始终可用键盘抵达，不依赖鼠标进入后才创建按钮。

资源右键 / Shift+F10 与 More 使用相同操作和可用性规则。Remove 调用原删除影响计算及确认流程；Pinned 继续支持原生拖动和菜单上下移动。高对比状态使用系统前景 / 背景配对，状态不单靠颜色。

Dashboard 与 Settings 使用原生 NavigationViewItem，分别位于菜单首项与 FooterMenuItems，继承平台收缩导航行为。收缩侧栏宽 64 DIP；底部活动数量改为 32 DIP 圆形数字徽标，超过 99 显示 `99+`，tooltip 与可访问名称保留完整数量。展开时显示完整状态文字。

规则编辑使用共享 DialogService 队列与统一 Windows 11 对话框样式。校验失败保持打开并聚焦出错字段；Cancel / Escape 只放弃当前规则编辑；Apply 更新页面草稿并返回列表。离开页面或关闭窗口时取消对话框，避免遗留浮层。

## Page composition

- Dashboard：Quick forward 为唯一主操作卡片。宽屏按 Server、Pin、Local / Remote 下拉框、Port、Forward 排成一行；卡片变窄时 Server / Pin 独占首行，其余三个控件一起换行。Advanced 使用 ToggleSwitch，Open terminal 直接显示；辅助解释收进 Help。Active tunnels 显示当前工作；Recent activity 与简短 Usage 位于下层。无数据时只有一句状态和必要 CTA。
- Settings：采用 Gallery 的独立 TextBlock 标题搭配 DevWinUI 10.1.0 SettingsCard / SettingsExpander，不使用 SettingsGroup 的内置 Header。Appearance、Forwarding、Server authentication 分区标题与卡片左缘对齐。滚动内容受页面可用宽度约束；窄屏时卡片纵向排列、下拉框拉伸，密钥卡片也约束内容宽度。认证选择立即保存；密钥模式才显示路径及 Browse / Save，保留原自动扫描和保存规则。
- Import：File 路径、JSON / SSH config ComboBox、Browse 在同一行。路径或格式改变后自动生成预览，手输路径采用短延迟并取消旧请求；选择同一路径或在路径框按 Enter 可重新读取。没有独立 Preview 步骤；Import 仍需有效预览，存在不可用条目时仍需确认跳过。保存状态变化后自动更新预览。
- Export：保留现有“选择范围 → 查看预览 → 导出”的流程。
- 各页宽度变化只调整布局，不重建 VM 或清空草稿。保留 M13 对虚拟化与焦点重入问题的修正。
- Settings 和 Dashboard 的 ScrollViewer 直接承载随视口拉伸的 Grid，最大内容宽度放在内部容器；避免带 MaxWidth 的 Stretch 内容直接触发滚动容器的错误居中。宽度检查需包含侧栏保持收缩时跨越最大内容宽度的组合。

## References

- [DevWinUI SettingsCard / SettingsExpander](https://ghost1372.github.io/DevWinUI/controls/settingsCard/index.html)
- [DevWinUI SettingsGroup](https://ghost1372.github.io/DevWinUI/controls/settingsGroup/index.html)
- 本机 DevWinUI Gallery 10.1.0 SettingsGroup 示例，截图 `.test-output/devwinui-settings-reference.png`。
- winapp 0.6.0 官方 portable：`gallery-contentdialog-1`、`gallery-gridview-2`、`gallery-menuflyout-5`。本机默认 CLI 和可下载的官方 0.6.0 尚无 find-api 子命令；按该 skill 的元数据核对原则直接读取当前 NuGet 的 API XML / generic.xaml，并由编译器验证实际引用。

## Validation scope

用户后续明确要求：UI 剧烈迭代期间默认仅构建、启动和少量相关交互验证，设计稳定后逐步恢复全量自动化。保留以下覆盖目标与现有脚本，不把它们当作每轮视觉修改的强制全量门槛。

构建与发布路径随 UI 项目迁至 `src/TunnelDeck`；原 M10–M13 未提交工作保留。使用隔离样例验证导航、右键 / More、排序、删除取消 / 确认、Quick Forward、设置持久化、导入 / 导出、宽窄窗、Light / Dark / 高对比和大集合虚拟化。测试布局变化时更新定位器，不削弱功能断言。最终打开新版独立评测实例。M13 中未验证的干净机器、实际 Narrator 语音、多显示器及系统文字缩放保持单独列示。
