# TunnelDeck WinUI 3 UI/UX 与实现规范

## 1. 项目定位

TunnelDeck 是一个轻量级 Windows 11 SSH 端口转发管理工具。

它不是服务器运维面板，也不是完整 SSH 客户端。核心目标是尽可能降低日常 SSH 端口转发的操作成本。

核心工作流必须始终保持为：

**打开 TunnelDeck → 使用默认/固定 Remote Server → 输入端口 → 启动转发。**

应用的其他功能，包括 Remote Server 管理、一键转发配置、历史记录、统计信息、导入导出等，都属于对这一核心工作流的辅助。

UI 设计必须体现以下优先级：

1. Quick Forward
2. Active Tunnels
3. Forward Profiles / Remote Servers
4. Recent Activity
5. Statistics
6. Settings / Import / Export 等管理功能

禁止为了“丰富界面”而让低优先级信息占据过多视觉空间。

------

# 2. 总体视觉风格

TunnelDeck 应采用标准 Windows 11 / WinUI 3 / Fluent Design 风格。

目标是：

- 紧凑；
- 清晰；
- 原生；
- 克制；
- 层次明确；
- 适合开发者工具；
- 避免 Web Dashboard 风格；
- 避免过度卡片化；
- 避免大量自定义视觉元素。

整个应用应更接近 Windows Terminal、Dev Home、WinUI Gallery 一类 Windows 原生生产力工具，而不是传统 SaaS 管理后台。

------

# 3. 全局设计准则

## 3.1 优先使用 WinUI 原生控件

优先使用：

- TitleBar
- NavigationView
- ListView
- ScrollViewer
- ComboBox
- NumberBox
- TextBox
- ToggleSwitch / CheckBox
- Button
- AppBarButton
- MenuFlyout
- Flyout
- ToolTip
- InfoBadge
- InfoBar
- ContentDialog
- TeachingTip（仅在确有需要时）

如果 WinUI 已有适合的原生控件，不应自行实现对应组件。

------

## 3.2 禁止随意硬编码视觉参数

除非规范明确要求，否则不得自行硬编码：

- 任意颜色；
- 任意阴影；
- 任意 Border；
- 任意 CornerRadius；
- 任意控件高度；
- 任意字号；
- 任意特殊背景色。

优先使用 WinUI Theme Resource。

例如不要：

```xaml
Background="#202020"
Foreground="#AAAAAA"
CornerRadius="13"
```

应尽可能使用系统 Theme Resource。

界面必须正确适配：

- Light Theme；
- Dark Theme；
- High Contrast。

------

## 3.3 Material

App Window 使用 Mica 作为基础材质。

页面 Root 不应随意添加完全不透明背景覆盖 Mica。

只有确实需要形成 Surface 层次的区域才使用系统 Card / Layer Fill Brush。

不要把：

- Quick Forward；
- Active Tunnels；
- Recent；
- Statistics；
- Settings Section；

全部分别塞进大圆角 Card。

优先通过：

- Typography；
- 空间；
- Separator；
- Subtle background；

形成层级。

------

# 4. 全局间距系统

主要使用以下间距：

```text
4
8
12
16
24
32
```

单位均为 epx。

禁止出现没有明确原因的：

```text
13
17
19
27
```

之类随机 Margin / Padding。

默认页面左右 Content Padding：

```text
24 epx
```

窄窗口可以降低为：

```text
12–16 epx
```

Section 之间通常使用：

```text
24–32 epx
```

控件内部相关元素通常使用：

```text
4–12 epx
```

------

# 5. Typography

页面名称使用 Heading 风格。

例如：

```text
Dashboard
Profiles
Servers
Web Development
dev-linux
Settings
```

普通字段标题、列表 Primary Text 使用 Body / Body Strong 等标准 Text Style。

辅助信息使用 Secondary Text / Caption。

端口、IP、Host、Fingerprint 等技术信息可以使用等宽字体，以强化可读性，例如：

```text
localhost:3000
192.168.1.20:22
SHA256:...
```

不要过度使用粗体。

------

# 6. App Shell

整个应用采用：

```text
TitleBar
+
NavigationView
+
Content Frame
```

作为基本 Shell。

页面结构：

```text
AppShell
├── TitleBar
├── NavigationView
│   ├── Dashboard
│   ├── Resource Area
│   │   ├── Profiles
│   │   └── Servers
│   ├── Settings
│   └── Runtime Status
└── Content Frame
```

------

# 7. TitleBar

TitleBar 高度：

```text
appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
```

布局从左到右：

```text
Hamburger
TunnelDeck
Drag Region / Flexible Space
Search
System Caption Buttons
```

最终视觉大致：

```text
☰  TunnelDeck                         [ Search... ]   ─ □ ×
```

暂时不显示 App Icon。

------

## 7.1 Hamburger

Hamburger 负责展开 / 折叠 NavigationView。

不要单独实现第二套 Pane 状态。

------

## 7.2 App Title

显示：

```text
TunnelDeck
```

始终靠近 Hamburger 左侧区域。

不要：

- 居中；
- 放在右边；
- 放到 Caption Button 附近。

------

## 7.3 Search

Search 位于 TitleBar 右侧，但在系统 Caption Buttons 左边。

它是全局搜索，而不是页面内搜索。

搜索对象：

- Forward Profiles；
- Remote Servers。

建议 Placeholder：

```text
Search profiles and servers
```

宽窗口：

```text
[ Search profiles and servers... ]
```

中等窗口：

```text
[ Search... ]
```

极窄窗口允许退化为 Search Icon。

Search 不应该占据整个 TitleBar 中央区域，应保留足够 Drag Region。

------

# 8. NavigationView / Sidebar

Sidebar 分为三个固定功能区：

```text
Function Area 1
Dashboard

────────────

Function Area 2
Profiles
Servers

────────────

Function Area 3
Settings
Runtime Status
```

功能区之间使用 Separator。

------

# 9. Sidebar Function Area 1

仅包含：

```text
Dashboard
```

Dashboard 始终可见。

不允许因为中间列表过长而滚动出屏幕。

------

# 10. Sidebar Function Area 2

包含两个固定 Section：

```text
Profiles
Servers
```

这里不是完整资源浏览器，而是 Working Set / Quick Access。

不显示所有 Profile 或 Server。

------

## 10.1 Section 不使用 Expander

使用普通静态 Header：

```text
PROFILES
```

和：

```text
SERVERS
```

不要提供 Collapse / Expand。

目前 Section 数量少且列表有限，引入 Expander 会增加没有必要的状态。

------

## 10.2 Sidebar 显示规则

每个 Section 显示：

```text
Pinned + Recent
```

Recent 不单独显示 Heading。

Pinned 和 Recent 不拆成两个视觉列表。

例如：

```text
PROFILES

📌 Web Development
📌 Database
   Rust Project
   Backend
   Personal

All profiles >
```

Pin 图标可以作为轻量提示。

Recent item 不需要专门显示 Recent Badge。

------

## 10.3 Sidebar 最大项目数

Settings 中提供：

```text
Maximum items per section
```

默认：

```text
5
```

建议允许：

```text
3–10
```

这是每个 Section 的独立上限。

不是 Profiles + Servers 共用一个数量。

------

## 10.4 Sidebar item 生成算法

规则：

```text
1. 先取 Pinned
2. 按 Pinned List 中的顺序
3. 如果数量不足 MaximumItems
4. 使用 Recent 补齐
5. Recent 按 LastUsedAt DESC
6. 已经出现在 Pinned 中的项目必须去重
7. 最终数量最多 MaximumItems
```

概念代码：

```text
visible =
    Pinned.Take(MaximumItems)

remaining =
    MaximumItems - visible.Count

visible +=
    Recent
        .Where(x => !visible.Contains(x))
        .OrderByDescending(LastUsedAt)
        .Take(remaining)
```

如果：

```text
Pinned.Count >= MaximumItems
```

则 Sidebar 只显示 Pinned List 的前 MaximumItems 项。

Pinned 顺序因此直接决定 Sidebar 优先级。

------

# 11. Sidebar Scroll 行为

Sidebar 结构建议：

```text
Grid
Row 0 = Auto   Dashboard
Row 1 = Auto   Separator
Row 2 = *      Resource Area
Row 3 = Auto   Separator
Row 4 = Auto   Footer
```

只有 Resource Area 可以滚动。

Profiles 和 Servers 整体位于一个统一 ScrollViewer 中：

```text
ScrollViewer
    StackPanel
        Profiles Section
        Servers Section
```

禁止：

- Profiles 自己一个 ScrollViewer；
- Servers 自己一个 ScrollViewer；
- Nested ScrollViewer；
- 动态根据窗口高度计算当前能显示多少条资源。

资源数量始终遵守 MaximumItems。

窗口高度不足时只让整个 Function Area 2 滚动。

Dashboard、Settings 和 Runtime Status 始终保持可见。

------

# 12. Sidebar All 页面入口

以下入口始终存在：

```text
All profiles
All servers
```

不要只有 overflow 时才显示。

它们不是 overflow indicator，而是固定的 Resource Management Entry。

------

# 13. Sidebar Footer

底部包含：

```text
Settings
N active
```

例如：

```text
⚙ Settings
● 3 active
```

目前 Runtime Status 只显示：

```text
N active
```

不要提前加入：

- 实时带宽；
- 总流量；
- Uptime；
- 其他统计。

以后如有需要再扩展。

------

# 14. Dashboard 页面

Dashboard 是 App 主页面。

页面结构从上到下：

```text
Dashboard

Quick Forward

Active Tunnels

Recent + Statistics
```

视觉优先级：

```text
Quick Forward
>
Active Tunnels
>
Recent / Statistics
```

------

# 15. Quick Forward

Quick Forward 是整个 App 最核心的操作区域。

应位于 Dashboard 顶部。

需要让用户完成以下高频流程：

```text
选择/使用默认 Server
→ 输入端口
→ Enter
→ Forward
```

------

## 15.1 Server

提供 Remote Server Selector。

用户可以固定当前 Server。

同时 Settings 可以配置：

```text
Default Server
```

进入 Quick Forward 时优先使用：

```text
当前已经固定/选择的 Server
→ Settings.DefaultServer
→ 未选择
```

选择状态在合理情况下应跨页面保持。

------

## 15.2 Open Terminal

Quick Forward 区域提供：

```text
Open Terminal
```

使用当前 Remote Server 打开：

- Windows Terminal；
- 或配置的终端程序。

该功能属于 Secondary Command。

视觉层级不得高于 Forward。

------

# 16. Forward Mode

支持：

```text
Local Forwarding (-L)
Remote Forwarding (-R)
```

UI 不要只显示：

```text
-L
-R
```

必须同时显示人类可读名称。

------

# 17. 自动寻找可用端口

Quick Forward 和 Profile Rule Editor 都提供：

```text
Find next available port if occupied
```

Settings 中提供全局默认值：

```text
Automatically find next available port when occupied
```

创建新的 Quick Forward / Rule 时使用该设置作为默认值。

用户可以在单次 Rule 上覆盖。

------

# 18. Auto Port Search 行为

当指定端口不可用时：

```text
Requested = N
```

从：

```text
N + 1
```

开始递增寻找。

直到：

- 找到可用端口；
- 或达到实现定义的搜索上限。

成功时：

```text
Requested Port != Actual Port
```

必须在 UI 中清楚标记。

禁止默默修改端口而不通知用户。

------

# 19. Port Migration / Adjustment Indicator

正常情况：

```text
Local :3000 → Remote :3000
```

如果 Local Port 被调整：

```text
Local :3001 [!] → Remote :3000
```

如果 Remote Port 被调整：

```text
Local :3000 → Remote :8001 [!]
```

其中：

```text
[!]
```

使用带感叹号 Icon 的 Attention InfoBadge。

不要使用 numeric InfoBadge 包住端口。

端口数字仍然正常显示。

------

## 19.1 Hover

鼠标悬停 Attention InfoBadge 时显示 ToolTip：

```text
Port automatically adjusted

Requested local port: 3000
Assigned local port: 3001
Port 3000 was already in use.
```

------

## 19.2 Click

点击 InfoBadge 打开轻量 Flyout。

例如：

```text
Port adjusted

Requested
3000

Assigned
3001

Port 3000 was already in use.
TunnelDeck selected the next available port.
```

InfoBadge 表示：

```text
实际执行结果和用户请求存在差异
```

不代表 Tunnel 处于错误状态。

因此 Tunnel 本身仍然可以显示：

```text
Active
```

不要因为出现 InfoBadge 就把整条 Tunnel 改成 Warning。

------

# 20. Active Tunnels

Active Tunnels 显示当前所有正在运行的 Tunnel。

不要使用传统 Enterprise DataGrid 风格。

优先使用紧凑 ListView Row。

每个 Row 只显示运行时真正重要的信息：

- Remote Server；
- Forward Mode；
- Local Port；
- Remote Port；
- Status；
- Port Adjustment Indicator；
- Stop / Secondary Action。

不要默认显示：

- Authentication Method；
- Started Time；
- Duration；
- Key Path；
- SSH Fingerprint；
- 其他连接参数。

------

# 21. Active Tunnel Row

示例：

```text
● dev-linux                  Local forwarding

Local :3001 [!] → Remote :3000              [Stop] [...]
```

或者更紧凑：

```text
● dev-linux
  Local :3001 [!] → Remote :3000             ■   ...
```

列表项不应过高。

------

# 22. Active Tunnel Details

点击 Active Tunnel Row 或 Details Command 时使用 Flyout。

不要使用 ContentDialog。

Flyout 内容可包含：

```text
Server
Forward Mode
Requested Port
Actual Port
Started At
Duration
Source Profile
Status
```

有 Profile 时显示来源，例如：

```text
Started from
Web Development
```

临时 Quick Forward 则可以显示：

```text
Started from
Quick Forward
```

------

# 23. Recent Activity

Dashboard 下半部分包含最近转发。

当前不单独设计 History 页面。

Recent Activity 可以显示：

```text
Server
Port Route
Profile（如果有）
Time
```

例如：

```text
Web Development
dev-linux · Local :3000 → :3000
3 min ago
```

或者：

```text
dev-linux
Local :8080 → :8080
Yesterday
```

Recent Activity 不承担完整日志功能。

------

# 24. Statistics

Statistics 与 Recent Activity 位于同一个较大的 Dashboard Section 中。

宽窗口左右两栏：

```text
Recent Activity       Statistics
```

例如 Statistics：

```text
Total forwards
Total transferred traffic
Most used profile
Most used server
```

Statistics 属于低优先级信息。

视觉层级不得超过 Active Tunnels。

不要为每个指标创建巨大 KPI Card。

------

# 25. All Profiles 页面

页面结构：

```text
Profiles                          [Add] [...]
```

More Menu：

```text
Import
Export
────────────
Delete all profiles
```

Delete All 使用 destructive / critical semantic foreground。

不要硬编码红色。

------

# 26. All Profiles 内容结构

页面由：

```text
Pinned
Pinned List

[extra spacing]

Other List
```

组成。

Other 不显示：

```text
Other
```

Heading。

因此视觉上表现为：

```text
Profiles                         [Add] [...]

Pinned

⠿ Web Development          📌  ...
⠿ Database                 📌  ...


Rust Project                    ...
Backend                         ...
Personal                        ...
```

------

# 27. Pinned Section

Pinned Section：

- 显示 `Pinned` Heading；
- 支持 Drag Reorder；
- 顺序持久化；
- 直接影响 Sidebar 中 Pinned 的顺序；
- Pin 新项目时添加到列表底部；
- Pinned 为空时整个容器隐藏。

Pinned 为空时：

```text
Profiles                         [Add] [...]

Rust Project
Backend
Personal
```

不要显示：

```text
Pinned
No pinned items
```

也不要显示空 Placeholder。

------

# 28. Pinned / Other 视觉间距

Pinned Section 的容器负责提供：

```text
Bottom Margin / Padding
```

用于与 Other List 拉开距离。

不要给 Other List 增加固定 Top Margin。

原因：

Pinned Section 为 Collapsed 时，其附带 spacing 应一起消失。

------

# 29. Other List

Other 不显示 Heading。

Other：

- 不支持拖动排序；
- 不按最近使用动态排序；
- 使用 insertion order；
- 当前内存顺序就是显示顺序。

Recent 使用行为不得改变 Other 顺序。

------

# 30. Other Insertion Order

新建 Resource：

```text
insert at Other[0]
```

Unpin：

```text
remove from Pinned
insert at Other[0]
```

正常访问资源：

```text
do nothing
```

Recent metadata：

```text
update LastUsedAt
```

但不得改变 Other List 顺序。

------

# 31. Pin 行为

Pin：

```text
remove from Other
append to Pinned
```

即：

```text
Pinned.Add(item)
```

新 Pin 的项目位于 Pinned 最底部。

如果用户希望提高 Sidebar 优先级，需要手动拖动排序。

------

# 32. Unpin 行为

Unpin：

```text
remove from Pinned
insert at Other[0]
```

视为该 Resource 重新进入 Other 集合。

------

# 33. List 持久化

不需要额外依赖：

```text
CreatedAt
PinnedOrder
OtherOrder
```

来重新构造显示顺序。

集合在内存中的顺序即 UI 顺序。

序列化时按照当前集合顺序保存。

反序列化时按照磁盘文件顺序恢复。

例如：

```text
PinnedProfileIds:
[
    "profile-a",
    "profile-b"
]

OtherProfileIds:
[
    "profile-f",
    "profile-c",
    "profile-d"
]
```

同理：

```text
PinnedServerIds
OtherServerIds
```

Recent 独立保存：

```text
LastUsedAt
```

Sidebar 使用 Recent Metadata 计算展示项。

All 页面不根据 LastUsedAt 排序。

------

# 34. Pinned Drag Reorder 实现原则

Pinned 与 Other 不要实现成同一个 grouped ListView。

推荐：

```text
Pinned ListView
Other ListView
```

两个视觉上连续的数据区域。

Pinned ListView：

```text
CanReorderItems = true
AllowDrop = true
```

Other ListView：

```text
CanReorderItems = false
```

拖动只表示：

```text
改变 Pinned 顺序
```

不使用拖动完成：

```text
Pin
Unpin
```

Pin / Unpin 应通过：

- Pin Action；
- Context Menu；
- More Menu；

完成。

------

# 35. Profile List Item

示例：

```text
Web Development
4 forwarding rules                     📌   ...
```

Secondary Text 可以显示：

```text
4 forwarding rules
```

不要默认显示过多详情。

------

# 36. All Servers 页面

结构与 All Profiles 页面保持一致。

```text
Servers                           [Add] [...]
```

More Menu：

```text
Import
Export
────────────
Delete all servers
```

如果 Import 的主要对象是 SSH Config，可以写成：

```text
Import from SSH config
```

具体 Import / Export 文件格式属于数据层规范，不应影响页面布局。

------

# 37. Server List Item

示例：

```text
dev-linux
ivy@192.168.1.20:22                  📌   ...
```

名称为 Primary Text。

Endpoint 为 Secondary Text。

------

# 38. Empty State

如果整个 Profiles 集合为空：

```text
No profiles yet

Save groups of forwarding rules
and start them together.

[Add profile]
```

如果 Servers 集合为空：

```text
No servers yet

Add a remote SSH server to start forwarding ports.

[Add server]
```

Empty State 只在整个集合为空时出现。

Pinned 为空不显示 Empty State。

------

# 39. Profile Detail / Editor 页面

Profile 是：

```text
多个 Forward Rule 的逻辑容器
```

它不绑定单一 Server。

一个 Profile 可以包含多个 Server 的 Rules。

------

# 40. Forward Profile 数据模型

概念模型：

```text
ForwardProfile
{
    Id
    Name
    Rules[]
}
```

Rule：

```text
ForwardRule
{
    Id
    RemoteServerId
    Mode
    LocalPort
    RemotePort
    AutoFindAvailablePort
}
```

------

# 41. Profile Editor 页面

顶部：

```text
Web Development
```

Heading。

可提供：

```text
Start all
More
```

正文：

```text
Name

Forwarding Rules
```

例如：

```text
Web Development

Name
[ Web Development ]

Forwarding rules

dev-linux     Local     :3000 → :3000        ...
dev-linux     Local     :5173 → :5173        ...
database      Local     :5432 → :5432        ...

[Add rule]
```

------

# 42. 新增 Rule 时的 Server 自动补全

新增 Rule 时，Server 默认值按以下优先级决定：

```text
1. 当前 Profile 上一条 Rule 的 Remote Server
2. 当前 Quick Forward 固定/选中的 Server
3. Settings.DefaultServer
4. 未选择
```

最重要的是：

如果上一条 Rule 已有 Server：

```text
新 Rule 自动继承上一条 Server
```

这样用户连续创建同 Server 的多个端口规则时无需反复选择服务器。

这只是 UI 默认值。

数据模型仍允许每条 Rule 使用不同 Server。

------

# 43. Rule Editor

Rule 应至少包含：

```text
Remote Server
Mode
Local Port
Remote Port
Auto Find Available Port
```

普通列表 Row 保持紧凑。

只有进入编辑状态时再显示完整字段。

例如普通 Row：

```text
dev-linux     Local     :3000 → :3000       Auto ✓    ...
```

不要让每个 Rule 永久占据一个大型 Form Card。

------

# 44. Remote Server Detail 页面

Remote Server 是独立 Resource。

页面顶部：

```text
dev-linux
ivy@192.168.1.20:22
```

可提供：

```text
Test Connection
Open Terminal
More
```

------

# 45. Remote Server 字段

至少包含：

```text
Display Name
Host / IP
User
Port
Authentication Method
```

Display Name 可以为空。

为空时自动使用：

```text
Host / IP
```

作为显示名称。

允许重名。

真实身份始终依赖：

```text
UUID / ServerId
```

不要使用 Display Name 作为唯一标识。

------

# 46. Authentication

认证方式：

```text
Password
Private Key / Identity File
```

不要称为：

```text
SHA256 authentication
```

SHA256 只用于类似 SSH Fingerprint 的显示。

可以显示：

```text
Fingerprint
SHA256:...
```

但它不是 Authentication Method。

------

# 47. SSH Config

TunnelDeck 内部 Server Model 独立于：

```text
~/.ssh/config
```

支持 Import。

如果未来实现反向同步，不应把整个任意 SSH Config 简化成可无损 round-trip 的普通表单。

尽量只管理 TunnelDeck 自己负责的配置部分。

Import / Export UI 位于 All Servers 页的 More Menu。

------

# 48. Settings 页面

当前至少提供：

## General / Sidebar

```text
Maximum items per section
[5]
```

范围：

```text
3–10
```

## Forwarding

```text
Find next available port when occupied
[On / Off]
```

这是创建新 Forward Rule 和 Quick Forward 的默认值。

## Default Server

```text
Default Remote Server
[ dev-linux ▼ ]
```

如果 Default Server 被删除：

```text
DefaultServerId = null
```

不要保留 dangling reference。

------

# 49. 删除单个 Remote Server

删除 Server 之前必须调用：

```text
CalcServerDeletionImpact(serverId)
```

禁止直接删除后再处理影响。

------

# 50. CalcServerDeletionImpact()

概念返回结构：

```text
ServerDeletionImpact
{
    ServerId

    AffectedProfiles[]
    AffectedRules[]

    ActiveTunnels[]

    AffectedProfileCount
    AffectedRuleCount
    ActiveTunnelCount
}
```

Affected Profile 应能进一步显示：

```text
Profile Name
Affected Rules
```

Affected Rule 应至少包含：

```text
Mode
Local Port
Remote Port
```

------

# 51. 单 Server 删除确认

删除一个 Server 时详细展示受影响信息。

例如：

```text
Delete "dev-linux"?

2 active tunnels will be stopped.

3 forwarding rules will be removed:

Web Development
  Local :3000 → :3000
  Local :5173 → :5173

Database
  Local :5432 → :5432

This action can't be undone.

[Cancel] [Delete]
```

如果：

```text
ActiveTunnelCount == 0
```

不要显示：

```text
0 active tunnels will be stopped
```

如果：

```text
AffectedRuleCount == 0
```

不显示 Rules Section。

------

# 52. Rule 数量很多时

单 Server 删除原则上展示详细影响。

但如果条目非常多，不要让 ContentDialog 无限增长。

可以：

```text
Profile A — 8 rules
Profile B — 5 rules
Profile C — 4 rules
+ 23 more
```

并允许：

```text
Show details
```

或使用 Dialog 内 ScrollViewer。

------

# 53. Delete All Servers

Delete All 不展示每条 Rule 的详细信息。

只显示数量摘要：

```text
Delete all servers?

12 servers will be deleted.
4 active tunnels will be stopped.
27 forwarding rules in 6 profiles will be removed.

This action can't be undone.

[Cancel] [Delete all]
```

底层应复用相同 Impact Calculation 逻辑。

不要实现两套独立的依赖扫描逻辑。

------

# 54. Server 删除执行顺序

确认之后：

```text
1. Stop affected active tunnels
2. Remove affected Forward Rules
3. Delete Remote Server
4. Clear DefaultServerId if needed
5. Persist state
6. Refresh UI
```

删除多个 Servers 时同理。

------

# 55. Active Tunnel 停止失败

如果某个受影响 Active Tunnel 无法正常停止：

```text
中止删除
```

不要：

```text
部分 Tunnel 停止
部分 Rule 删除
Server 又删除了一半
```

应尽可能保证整个删除动作的业务一致性。

------

# 56. Server 删除的级联边界

删除 Server 会：

```text
Stop Active Tunnels
Delete Forward Rules referencing Server
Delete Server
```

不会：

```text
Delete Forward Profile
```

即使某个 Profile 因此变成：

```text
0 Rules
```

仍然保留 Profile。

------

# 57. Delete All Profiles

Delete All Profiles 不涉及 Server 删除。

确认：

```text
Delete all profiles?

12 profiles will be permanently deleted.

This action can't be undone.

[Cancel] [Delete all]
```

如果 Profile 启动产生的 Active Tunnel 仍在运行，是否停止应根据 Tunnel 生命周期模型单独决定。

如果 Active Tunnel 与 Profile 只存在“来源信息”关系而不依赖 Profile 对象，则删除 Profile 不应自动停止 Tunnel。

------

# 58. Destructive Command 规范

以下操作必须使用明确 destructive semantic：

```text
Delete
Delete all
Clear persistent data
```

不要让 Delete 和 Add 获得同样视觉权重。

All 页面顶部只直接显示：

```text
Add
More
```

Destructive 命令隐藏在 More Menu 中。

------

# 59. ContentDialog 使用范围

ContentDialog 只用于：

- 删除；
- Delete All；
- 其他不可逆操作；
- 必须阻断用户继续操作的重要确认。

不要用于：

- 查看 Tunnel 信息；
- 查看迁移信息；
- 普通提示；
- 成功通知。

普通详情使用 Flyout。

普通操作结果使用 InfoBar / Inline Status / Tooltip。

------

# 60. Flyout 使用范围

Flyout 用于轻量 Context Details。

包括：

```text
Active Tunnel Details
Port Adjustment Details
```

Flyout 不应承担复杂表单编辑。

------

# 61. InfoBar 使用范围

InfoBar 可以用于：

```text
Connection failed
Unable to create tunnel
Import completed with warnings
Port search exhausted
```

Routine Success 不需要频繁弹 InfoBar。

成功状态尽量直接反映在页面内容中。

------

# 62. Keyboard / Focus

TunnelDeck 是开发者工具，应支持良好的键盘操作。

重点：

```text
Quick Forward Port Input
```

在 Server 已确定时，应允许：

```text
输入端口
Enter
```

直接开始 Forward。

不要要求：

```text
输入 → Tab 多次 → 点击按钮
```

Flyout：

```text
Esc
```

应关闭。

List Item、Pin、More、Stop 等操作必须具备合理 AutomationProperties / Accessible Name。

------

# 63. Responsive Layout

主要针对：

```text
1100–1300 epx
```

桌面宽度优化。

窗口变窄时优先：

```text
NavigationView Open
→ Compact
→ Minimal
```

而不是无限压缩 Content。

------

# 64. Dashboard Responsive

宽窗口：

```text
Recent Activity | Statistics
```

窄窗口：

```text
Recent Activity

Statistics
```

即从双栏自动切换为上下布局。

Quick Forward 和 Active Tunnels 始终保持完整可用。

------

# 65. TitleBar Responsive

宽：

```text
Search profiles and servers...
```

中：

```text
Search...
```

窄：

```text
Search Icon
```

系统 Caption Buttons 区域不可侵占。

------

# 66. 视觉层级禁止事项

Coding Agent 不得：

- 为每个 Section 创建巨大 Card；
- 给每个 List Item 加厚 Border；
- 到处使用 Accent Color；
- 为普通按钮添加 Gradient；
- 添加非 Fluent 风格阴影；
- 使用网页 Dashboard 样式 KPI Tiles；
- 使用随机圆角；
- 使用随机灰色；
- 用大量 Divider 强行切割内容；
- 使用超大 Heading；
- 为 Sidebar 添加复杂背景；
- 给每个状态加彩色胶囊；
- 把普通信息变成 Badge。

------

# 67. Accent Color 使用原则

Accent 主要用于：

- Selection；
- Primary Action；
- Active / Focus；
- 重要交互状态。

不要用于：

- 所有 Icon；
- 所有 Heading；
- 所有 List Item；
- 所有统计值。

------

# 68. Status Color 使用原则

状态不能只依赖颜色表达。

例如：

```text
● Active
! Warning
× Failed
```

同时包含：

```text
Icon + Text
```

确保 High Contrast 和色觉差异用户仍能理解。

------

# 69. 推荐页面结构

最终主要页面：

```text
DashboardPage

ProfilesPage
ProfileDetailPage

ServersPage
ServerDetailPage

SettingsPage
```

必要时 Add / Edit 使用同一个 Detail Page 模式。

不要为了新增操作创建过多 Navigation Destination。

------

# 70. ViewModel 职责

UI 不直接处理业务依赖扫描。

建议分层：

```text
UI / Page
ViewModel
Service / Domain
Persistence
```

例如：

```text
ServerDetailViewModel.DeleteAsync()
```

内部调用：

```text
ServerService.CalcServerDeletionImpact()
ServerService.DeleteServerAsync()
```

而不是 Page Code Behind 手工遍历所有 Profile。

------

# 71. Recent 数据

每个 Profile / Server 可以维护：

```text
LastUsedAt
```

它只用于：

```text
Sidebar Recent
Statistics
```

不得用于改变 All 页 Other List 顺序。

------

# 72. Runtime Tunnel 与 Persistent Config 分离

Persistent：

```text
RemoteServer
ForwardProfile
ForwardRule
Settings
```

Runtime：

```text
ActiveTunnel
```

ActiveTunnel 至少包含：

```text
Id
ServerId
Mode

RequestedLocalPort
RequestedRemotePort

ActualLocalPort
ActualRemotePort

StartedAt
Status

SourceProfileId?
SourceRuleId?
```

Requested 与 Actual 必须分开保存，否则无法正确展示 Port Adjustment。

------

# 73. Active Tunnel Duration

不要持续把 Duration 写入磁盘。

运行时根据：

```text
Now - StartedAt
```

计算。

Flyout 打开时显示动态 Duration。

------

# 74. Persistence

应用关闭时：

```text
Stop all active tunnels
Persist persistent state
```

Active Tunnel 不作为下次启动自动恢复的持久状态，除非未来明确增加 Resume 功能。

------

# 75. App Shutdown

关闭 App：

```text
Stop all active tunnels
```

如果正常停止需要异步操作，应在窗口真正退出前完成合理的 shutdown sequence。

不要让 SSH 进程或后台 tunnel 在 App 退出后继续无意运行。

------

# 76. 最终产品行为原则

所有实现都必须围绕以下目标判断是否合理：

### 原则 1：核心路径最短

```text
Open
→ Port
→ Enter
```

能完成的事情，不要强迫用户进入复杂配置页。

### 原则 2：Sidebar 是 Working Set

Sidebar 不负责展示所有数据。

### 原则 3：All 页面负责管理

创建、删除、Pin、排序、Import、Export 属于 All 页面。

### 原则 4：Pinned 是主动优先级

Pinned 顺序由用户显式维护，并直接影响 Sidebar。

### 原则 5：Recent 是行为数据

Recent 只辅助 Sidebar 自动补位，不改变资源本身的顺序。

### 原则 6：不要隐藏自动行为

Port 被重新分配时必须明确提示。

### 原则 7：破坏性行为必须说明影响

删除 Server 前先计算影响，然后确认。

### 原则 8：资源删除不制造 dangling reference

Server 删除时同时清理引用它的 Forward Rules。

### 原则 9：详情不等于 Dialog

轻量详情使用 Flyout。

### 原则 10：WinUI 优先

能用系统组件解决的视觉和交互，不自行发明另一套设计语言。

------

# 77. Coding Agent 最终约束

实现 UI 时，如果规范没有明确指定某个视觉值，应按以下优先级决定：

```text
1. WinUI 原生默认行为
2. Fluent Design 官方约定
3. 现有 TunnelDeck Design Token
4. 最后才允许新增自定义样式
```

Coding Agent 不得为了“看起来更现代”而主动加入：

```text
Glassmorphism
Gradient
Neon
Oversized cards
Floating navigation
Custom title controls
Heavy shadows
Animated background
Arbitrary accent colors
```

TunnelDeck 的视觉目标不是“炫”。

目标是：

**像一个真正属于 Windows 11 的、长期可用的开发者效率工具。**


