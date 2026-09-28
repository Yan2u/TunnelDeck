# UI / UX 第一轮统一设计

用户确认功能总体可用，并要求保持功能行为、持续改进 WinUI 3 外观。未提交、未重置原 M10–M13 改动；本轮不是新增功能里程碑。

## 设计与实现

- 先编写 `docs/ui-design-system.md`，说明每种列表的数据字段、信息优先级、容器、交互、主题与响应式规则。资料来自官方 winapp Gallery samples、DevWinUI 文档和本机 Gallery 10.1.0。默认 CLI 无 find-ui，新下载的官方 portable 0.6.0 支持 samples；find-api 尚不可用，直接读取当前包 API XML / PE 元数据与主题 XAML 校对。
- UI 工程完整迁至 `src/TunnelDeck`。同步 slnx、ProjectReference、Run / Publish / Lifecycle 脚本、发布配置的 gitignore 例外及 README。历史评测包和旧版回退 worktree 保留原路径。
- `Themes/DesignResources.xaml` 集中卡片、主次文字、端点和对话框样式。`DialogService` 为所有对话框显式应用继承 `DefaultContentDialogStyle` 的样式，解决自定义 ContentDialog 回退旧方角 Acrylic 外观。窗口继续用 Mica；对话框用原生 Windows 11 内容层与按钮区域，不伪称 ContentDialog 自带 Mica backdrop。
- `QuietCollectionResources.xaml` 仅作用于含独立操作的集合，取消父容器 PointerOver / Pressed 填充竞争，保留原生按钮交互与键盘焦点。没有复制整个平台 ControlTemplate。
- Profiles / Servers 使用有界 GridView / ItemsWrapGrid，1–3 列卡片；保留两组滚动范围、虚拟化、Pinned 拖排和失败回滚。名称 / 端点为主体，认证方式或服务器数量为辅助。`ResourceActions` 统一 More、右键、Shift+F10 与侧栏菜单，并继续调用原删除影响确认与持久化逻辑。
- Dashboard 将 Quick forward 单独成卡片，模式与端点帮助收进可键盘打开的 Flyout；删除重复解释。`TunnelListItem` 强调端口路线、简短服务器名、状态图标与独立动作；异常、自动改端口保留。`RecentActivityListItem` 显示两行摘要，完整 Requested / Actual、时间、来源注释及错误可在详情中阅读。统计数值和含义不变。
- Settings 使用 DevWinUI 10.1.0 的 SettingsGroup / SettingsCard / SettingsExpander，认证与保存事件继续调用原 VM。窄窗显式切换 Vertical，避免默认横排把长标签挤成多行。新增依赖和 lockfile 已还原，没有升级原 Windows App SDK。
- Import / Export 使用统一 `TransferPreviewItem`、资源图标、导入决策和按需详情。缺失必填项的原因仍直接显示，跳过确认不变。两页均将表单与虚拟化预览分离，保持固定页脚，避免在表单焦点事件中同步重置包含表单的 ListView。
- 已验收表单仅统一列表容器并收起重复说明，编辑、验证、快捷键和保存规则保留。
- 导入输入区与预览滚动区最终分开，并在进入页面时聚焦文件路径，避免滚动获取焦点时改变 RadioButtons 的格式选择；两区高度有界，短窗口内输入仍可滚动到达。1000 项顶部 / 底部 / 顶部为 7 / 8 / 7 个可访问实例，10 次格式 / 焦点循环通过。
- 原生标题栏按钮前景跟随实际 WinUI 文字画刷，修正应用切到 Dark 后系统仍用 Light 标题栏文字的问题。

## 验证

持续记录在 `docs/ui-refresh-validation.md` 与 `artifacts/ui-refresh`。使用隔离配置、保留的测试夹具和 .example 主机，不连接用户服务器。第一轮资源拖动测试因鼠标位置偏离目标 6 px 被驱动器拒绝，未放松校验；重新完整运行通过。卡片采用二维排列，既有拖放测试按实测目标 x/y 更新，失败回滚的视觉顺序断言同时检查行和列。

新增 `Verify-UiRefresh.ps1` 验证右键、键盘菜单、侧栏删除取消、活动详情、卡片 / 设置窄窗、主题和导入确认。保留旧功能套件并按新布局调整必要的控件可达性，不以截图替代连接 / 保存断言。

M13 的干净 Windows 安装、实际 Narrator 朗读、多显示器和 200% 系统字号证据缺口仍单列。本轮不重复无关性能压力测试，也不把用户认可功能理解为认可新视觉稿。

用户随后要求大幅精简当前 UI 重构阶段的自动化。此后保留完整脚本供设计稳定后恢复，采用构建 / 启动 / 少量相关交互检查。最终原生拖放 smoke 通过；卡片保留单选以供原生拖排，但通过轻量资源覆盖去掉多余选择边框，原生键盘焦点保持。

最终包为 `artifacts/ui-refresh/release-1/TunnelDeck-ui-refresh-1-win-x64.zip`。解压并校验 91 个文件，实际运行发布程序，确认 DevWinUI Settings、Dark 标题栏 / 删除对话框和取消不写配置。SHA-256 `5a2be23a8e666627df722fab3d64b39c23deef42be590bc325ef51c804f8aa55`。`Show-UiReview.ps1` 已打开独立样例评测窗口；最新数据与进程见 `artifacts/ui-refresh/user-review/review.json`。未签名、未向外发布、未提交代码。
