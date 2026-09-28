# Shell、导航与页面契约

更新：2026-09-23。M0–M13 与主要 UI 重构已完成用户评测，目前进行 Release x64 收尾。凭据见 [connections](connections.md)，转发界面见 [quick-forward](quick-forward.md)，规则编辑见 [profiles](profiles.md)，删除见 [deletion](deletion.md)，导入导出见 [transfer](transfer.md)，终端见 [terminal](terminal.md)，活动 / 设置见 [activity-and-settings](activity-and-settings.md)。

## 原生 Shell

`MainWindow` 使用 TitleBar + NavigationView + Frame。TitleBar 为 Tall 高度，保留系统 caption 和拖动区域；主页面只有一个 pane toggle，Back 使用 TitleBar 原生返回按钮。宽窗搜索在右侧，低于 600 epx 时使用带 AutoSuggestBox 的原生 Flyout；Ctrl+K 打开 / 聚焦搜索，Alt+Left 返回。

初始尺寸按 1200 × 760 epx 及显示器 DPI 换算，并限制在当前工作区内居中。没有按物理像素硬套布局，也没有修改系统窗口 chrome。

Dashboard 使用原生 NavigationViewItem，与 Profiles / Servers 的 header、working set 和 All 入口一起位于 MenuItems，可随该区域滚动。Settings 与运行状态位于固定页脚，滚动菜单不移动它们。收缩侧栏宽度为 64 DIP，保留 Dashboard / Settings 图标，运行状态切换为圆形数字徽标；各入口保留稳定的 AutomationId。没有替换 NavigationView 模板。

working set 直接调用 M2 的 `SidebarProjection`：每类分别取 Pinned，再补 Recent，数量遵守 3–10 上限。All 入口始终存在，访问页面不重排资源或伪造使用记录。

## 页面

| 页面 | 当前行为 |
| --- | --- |
| Dashboard | M7 已接入真实 Quick Forward / Enter / 高级端点、临时凭据、独立 Stop / Retry / Remove、调整与 Details Flyout、Recent 和基本启动统计；预览禁用网络命令 |
| Profiles / Servers | 原生 CommandBar、Pinned 与其余列表、集合空态、详情导航、Pin / Unpin、Pinned 原生拖排与键盘 Move up / down、失败保存的 Retry |
| ProfileDetail | 名称与规则草稿、原生 ContentDialog 规则编辑、Save / Cancel、单条 Start / Stop、跨服务器 Start all、逐条结果与 Retry failed |
| ServerDetail | 新增 / 编辑复用；连接字段与凭据保存、私钥文件选择、字段错误、可取消的 Test Connection、已验证指纹与身份占用提示；M5 已接入 |
| Settings | 窗口主题、Sidebar 上限、Local 自动找端口默认值、默认 Server |
| Import | All 页 More 进入；选择格式 / 文件、只读预览、警告与跳过确认、原子提交或取消 |
| Export | All 页 More 进入；选择资源范围、依赖说明、系统保存对话框与保存位置 |

M4 接入 M3 配置提交，使保存与 Pin 可用；M5 完成凭据编辑并在工作占用时锁定服务器连接身份；M7 / M8 将 Quick 与 Profiles 接入 M6 引擎。M9 在列表和详情的 More 中启用删除，统一经影响确认与事务协调；失败在 Shell 显示停止结果及 Review and retry。M11 在 Quick 与 Server More 中启用独立 Open terminal；详情有未保存连接参数时禁用，设计预览禁止外部连接。

详情页持有草稿，失败不发布到 Shell。M8 Profile 保存比较打开时的可编辑内容，允许无关 revision 和 LastUsedAt 更新；Server 仍沿用现有 revision / 身份门禁。保存完成仅在原编辑页面仍有效时切换路由。即时设置 / Pin 同样经过 ConfigurationService；重排还检查原 Pinned 顺序，失败恢复投影并显示 Retry。

## 导航与搜索

`Core/Navigation` 定义原有六种目的地，以及 M11 的 Import / ExportServers / ExportProfiles 路由，配合带可选 Guid 的 AppRoute 和唯一的 NavigationHistory。新增和编辑使用同一 Detail 目的地；null ID 表示新增。重复导航到当前路由不入栈；保存新增资源替换当前路由；资源失效时回到对应列表并清理不可用历史。Frame 的内部 history 关闭，避免两套栈冲突。

`Core/Presentation/ResourceSearch` 是无 UI 依赖的纯函数，检索 Profile 名称及 Server 名称 / host / username；支持忽略大小写和空白分词。精确名称、名称前缀、名称包含优先，随后按名称、资源类型、描述和 Guid 稳定排序。最多显示 12 项；空查询不返回资源。

结果带资源类型、ID、规则数或 endpoint。重名 Profile 带短 ID，完全相同名称与 endpoint 的 Server 也用短 ID 区分。选择结果只在 QuerySubmitted 导航，键盘选择建议不会提前跳页；无命中有明确提示。

M8 将这种区分同名资源的描述同步用于 All 页面、Server selector 和 Sidebar 的可访问名称 / ToolTip。配置发布后刷新已有搜索查询，资源改名或失效不会保留旧结果。

`ShellViewModel` 接收存储快照，通过 DispatcherQueue 发布给 UI，丢弃旧 revision，并在窗口关闭后忽略已排队回调。M7 将运行通知与资源快照通知分开，普通活动事件只更新稳定行与计数，不重建 Sidebar。Page/ViewModel 负责展示和草稿；模型校验、排序及事务仍由 Core / Infrastructure 执行。

## 布局与滚动

- 使用系统文字样式、ThemeResource、原生控件 chrome 和 Mica；没有自定义色板、阴影、整页卡片或 ControlTemplate。
- 页面默认 padding 24，内容区较窄时改为 16。Dashboard 按可用内容宽度在 680 epx 处重排 Quick Forward 字段，在 760 epx 处把 Recent / Statistics 从双栏改为上下排列。
- Dashboard 页面 ScrollViewer 负责正文，Recent 最多显示 5 项。M7 的活动 ListView 使用独立且有界的 viewport（页面高度 45%，限制 180–420 epx）；列表滚动不带动 Quick Forward。详情页规则列表仍关闭内部滚动。
- 资源页固定 CommandBar；Pinned / Other 使用两个有界原生 ListView，各自在自己的视口滚动。两组都有内容时 Pinned 最多占资源视口的 40%，只有 Pinned 时可使用全部。移除外层无限高度 ScrollViewer 后保留虚拟化，1000 Server + 1000 Profile 验证每页约 53 个行按钮。Pinned 使用原生单选和拖排，名称按钮负责导航；Other 拒绝拖放。Pinned 下间距随其容器隐藏，没有空组占位，菜单移动提供键盘等效操作。
- 表单长文本截断 / 换行，完整 endpoint 保留在可访问名称和 ToolTip 中；图标按钮有名称，状态和端口调整均有文字说明。
- Light / Dark 随主题资源切换。没有修改系统 High Contrast 设置，不将用户对 GUI A 的确认记成 Agent 已执行 High Contrast 实测。

## 隔离预览与验证

```powershell
./scripts/Run.ps1 -Preview Sample
./scripts/Run.ps1 -Preview Stress
./scripts/Run.ps1 -Preview Empty
./scripts/debug/Verify-Shell.ps1
./scripts/debug/Verify-Window.ps1
./scripts/debug/Verify-Storage.ps1
./scripts/debug/Verify-Dashboard.ps1
./scripts/debug/Verify-Forwards.ps1 -NoFixtureBuild
./scripts/debug/Verify-Profiles.ps1 -NoFixtureBuild
./scripts/debug/Verify-Resources.ps1
```

预览模式使用独立临时数据目录和凭据命名空间，顶部明确标注；可切换 Sample、Long lists、Empty。样本包含重名、长中英文名称、长 IPv6、多于 Sidebar 上限的 Pinned、空规则集合、端口调整和重连显示。Long lists 包含 24 个活动样本、其中 23 个 Running。样本复用 ActiveTunnel / TunnelItem 投影，后端和界面都禁止网络命令，状态不进入 AppState。

普通启动读取真实配置，活动列表初始为空，不自动恢复上次运行；用户提交 Quick 后由 `AppServices.Forwarding` 展示真实引擎状态。关闭先排空界面命令、停止引擎和连接，再刷新历史及配置。预览中的编辑只保存到隔离目录。`Verify-Shell.ps1` 显式使用 `.test-output/m4-shell/<guid>`，核对磁盘提交并重启验证。键盘注入先确认前台窗口 / 原生 popup 属于本次 App 进程，不向其他应用发送按键。

导航 / 搜索基线测试位于 `NavigationAndSearchTests`，历史证据在 [M4 evidence](evidence/2026-09-19-m4.json)。M7 输入、行投影与命令测试位于 QuickForwardTests / TunnelItemTests / TunnelWorkspaceTests，原始记录见 [M7 evidence](evidence/2026-09-20-m7.json)。[M8 evidence](evidence/2026-09-20-m8.json) 保留 Profile 编辑、批量启动和资源拖排验证。M9 增加真实删除后的路由 / 搜索清理、已删除来源导航禁用、长清单及关闭确认测试；当前汇总见 [milestones](milestones.md)。正式 GUI 评测不能用这些自动化结果替代。

控件依据：安装的 WinUI 2.3.9 metadata / generic.xaml，以及 [WinUI Gallery TitleBar](https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Samples/TitleBar/TitleBarPage.xaml) 和 [AutoSuggestBox](https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Samples/AutoSuggestBox/AutoSuggestBoxPage.xaml) 示例。现有 winapp CLI 0.3.1 用于 UIA smoke，未为 find-ui 升级机器工具链。
