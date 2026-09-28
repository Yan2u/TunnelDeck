# UI / UX 验证记录

## 2026-09-23 发布收尾

用户已认可主要 UI / UX，恢复了有代表性的回归。当前结果见 [收尾证据](evidence/2026-09-23-release-closeout.json)，性能与清理分别见 [性能](performance.md) 和 [清理记录](evidence/2026-09-23-release-cleanup.json)。

| 范围 | 本轮结果 |
| --- | --- |
| Release x64 | 锁定依赖构建通过；最终 UI 构建 0 warnings / 0 errors |
| Core / Infrastructure | 428 + 148 = 576 passed，无失败或跳过 |
| Shell / 资源卡片 | 页面导航、保存、搜索、Pin / Unpin、右键与 Shift+F10、480–1200 DIP 布局、一次真实拖动及顺序持久化通过 |
| Import / Export / 默认认证 | 系统选择器、自动预览、显式跳过、原子保存、失败重试、默认密钥与 IdentityFile 优先级、重启通过 |
| 真实 SSH / Profile | Local / Remote payload、独立 Stop、端口分配、失败重试、跨服务器规则、历史快照与退出清理通过 |
| 规则对话框 | 修复绑定初始化时序后，15 次连续打开、校验、应用 / 取消与保存通过；最终包另验 3 次 |
| 最终包的 Dashboard / Settings | Advanced 按需加载后草稿保留、模式切换、1200 / 500 DIP 排列、直接终端入口通过；Settings 在侧栏始终收缩的 1198 / 1200 / 1360 / 再次 1200 DIP 下留白正确 |
| ZIP 验证 | 84 个文件散列、23 个 XBF；含中文和空格的解压路径、PowerShell 5.1 启动 / 编辑、schema 升级备份与重启通过 |

本轮发现并解决两项问题。Shell 旧脚本仍把 Dashboard 当作固定页头；当前设计是原生菜单项，已改为检查固定页脚及滚回顶部后的 Dashboard 可达性。另有真实规则对话框崩溃：小型转储显示 `0x80004003`，随后对隔离进程取得的完整转储明确定位到 `RuleEditorDialog.OnOpened → RuleEditorView.FocusEditor`，此时 Model 尚未绑定。现在构造对话框时立即 `Bindings.Update()`，再允许 Opened 聚焦。诊断期间的全局异常记录已移除，没有吞掉异常或加入任意延时。

发布包为 `artifacts/release/TunnelDeck-1.0.0-win-x64.zip`，34,099,724 bytes，SHA-256 为 `766cd0232b29c3d71f0f4585949a1a5510a02fda8e62d7dbaf23470afc703153`。配置与重现命令见 [发布说明](release.md)。本地未签名、依赖共享运行库，没有上传外部服务。

按用户要求，本轮没有重复全部历史 SSH 场景、长时间压力、系统主题 / 文字缩放切换、跨显示器 DPI 或 Narrator 实际朗读；干净机器依赖安装仍未验证。功能与主要 UI 的人工认可来自用户，不由脚本替代。下面保留各轮历史过程；其中指向 artifacts 的旧截图、日志和中间发布包在本次清理后不再保留，长期摘要位于 docs/evidence。

## Dashboard 快速转发区调整

2026-09-23。Server、Pin、Local / Remote 下拉框、Port、Forward 在宽屏共用一行，窄屏按组换行；Advanced 改为开关，Open terminal 直接显示。详见 [改动记录](../.codex/history/2026-09-23-dashboard-quick-forward-layout.md)。

本轮仅执行 Release x64 构建和该区域的轻量检查：构建 0 警告、0 错误；模式切换、Advanced 字段与草稿保留、非法端口校验、终端入口可见性以及 1200 / 500 DIP 布局均通过。没有启动 SSH 或外部终端，也没有运行全量回归。证据在 `artifacts/dashboard-quick-layout`，最终隔离样例窗口停在 Dashboard。

## Settings 宽度阈值修复

2026-09-23 后续反馈确认：第三轮的 Settings 检查没有覆盖“较宽窗口且侧栏保持收缩”的组合，问题仍会在内容视口超过 1120 DIP 时发生。本次已复现并修正 ScrollViewer 直接承载带 MaxWidth 的 Stretch 内容所导致的错误居中；加入视口 Grid，将最大宽度约束放到内部内容。

使用未裁剪的实际布局边界，验证收缩侧栏下 1198 / 1200 / 1360 / 800 / 再次 1200 DIP。内容全部处于视口内且左右留白对称；1200 DIP 时错误的 109 DIP 左侧留白恢复为约 0.57 DIP。详见 [修复记录](../.codex/history/2026-09-23-settings-scroll-viewport-fix.md) 和 `artifacts/settings-viewport-fix/layout-checks.json`。临时诊断代码已移除，最终隔离样例窗口保持在 Settings，侧栏收缩。

## 第三轮：Import 自动预览与 Settings 窄屏布局

2026-09-23。Import 的路径、格式下拉框与 Browse 合并成一行，选中文件后自动预览；条目说明改为 SettingsExpander，行号 / 正文使用双列只读 TextBox，认证来源位于 ItemsFooter。Settings 修正滚动内容宽度、下拉框对齐和卡片在窗口变化后的视觉状态。详见 [第三轮历史](../.codex/history/2026-09-23-import-preview-and-responsive-settings.md)。

本轮只检查直接相关的范围，证据目录为 `artifacts/ui-refresh/iteration-3`：

- 原生 Visual Studio MSBuild Release x64 最终构建通过，0 警告、0 错误；`build-final.log`。
- 现有 SSH parser / 认证默认值测试 49 项通过；`parser-tests/import-parser.trx`。
- 导入：自动预览、展开行号及 footer、格式切换、跳过确认、实际提交，以及真实文件选择器选中文件后自动启用 Import；`interaction.json`、`import-final.png`。
- Settings：640 / 800 / 1200 DIP 下查看长服务器名与密钥路径，相关控件均在页面水平边界内，恢复宽屏时排列正确；`settings-layout.json`、`settings-*-final.png`。

最终使用独立输出和独立样例数据运行，窗口保持在 Import 页面；位置和进程见 `review.json`。本轮没有重新打发布包，也没有运行完整原生回归、SSH、压力或系统显示设置测试。首次条目容器异常已修复，诊断经过保留在第三轮历史中。

## 第二轮：设置、对齐、规则对话框与收缩侧栏

2026-09-22。设置页已改用独立 TextBlock 分区标题；Running 与三个操作按钮共用居中布局；新增 / 编辑规则移入 ContentDialog；Dashboard 与 Settings 改用原生导航项，收缩宽度 64 DIP，活动数量显示为居中的圆形数字。设计规则见 [设计系统](ui-design-system.md)，改动模块见 [第二轮历史](../.codex/history/2026-09-22-ui-refinement-2.md)。

遵照用户要求，本轮只做 Release 发布构建、隔离实例启动及下列少量检查，没有重跑单元测试全集、完整原生回归、SSH 连接、压力或系统显示设置测试。

- 设置页：查看独立标题与卡片的最终排列，见 `artifacts/ui-refresh/iteration-2/settings-settled.png`。
- 规则对话框：验证必填端口错误保持打开、取消不修改配置、应用后仅更新页面草稿、Save 后写入规则，以及再次编辑时正确回填端口。见 `rule-dialog.png`、`rule-smoke.json`。
- 隧道行：使用无 SSH 连接的样例；Running、Stop、Details、More 的垂直中心均与所在行一致，实测偏差 0 px。见 `tunnels.png`、`tunnel-alignment.json`。
- 最终收缩侧栏：Dashboard、Settings 与数字可见，徽标数字与导航项水平中心偏差 0 px；截图确认图标完整。见 `sidebar-final.png`、`sidebar-final.json`。

前三项使用本轮初次发布构建完成；之后仅补齐徽标容器宽度，重新发布并检查最终包启动和收缩侧栏。两个中间隔离实例已正常关闭，最终评测实例保持打开；其数据、可执行文件和进程见 `artifacts/ui-refresh/iteration-2/final-review/review.json`。

最终发布包：[TunnelDeck-ui-refresh-2-win-x64.zip](../artifacts/ui-refresh/release-2-final/TunnelDeck-ui-refresh-2-win-x64.zip)，34,390,876 bytes。SHA-256：`ca3913fa5aa360d0b952e8f17b579a032a990cce19b7696cbdbbb5eb4b08331c`。本地运行库要求与上一轮一致。代码尚未提交。

## 第一轮

本轮响应 2026-09-22 的视觉反馈，保持原有功能契约。设计与字段映射见 [设计规则](ui-design-system.md)，改动范围见 [历史记录](../.codex/history/2026-09-22-ui-design-foundation.md)。UI 工程已迁至 `src/TunnelDeck`。

## 已实施

- 所有 ContentDialog 使用同一个 Windows 11 模板，圆角、内容面与按钮区一致；主窗口仍为 Mica。
- Dashboard 的 Quick forward、Active tunnels、Recent activity 与 Usage 分层，解释按需打开；异常和实际端口调整继续可见。
- Profiles / Servers 自适应卡片网格；More、右键和 Shift+F10 菜单一致，侧栏也可 Pin / Unpin / Remove。卡片容器没有和内部按钮竞争的 hover 填充 / 边框。
- Settings 使用 DevWinUI 10.1.0 的 SettingsGroup / SettingsCard / SettingsExpander；窄窗纵向排列。默认认证和密钥的保存语义不变。
- Import / Export 共享预览项，保留虚拟化与固定操作区；必填项错误直接显示，其他提示可展开查看。

## 检查记录

用户已明确要求在 UI / UX 剧烈迭代期间精简自动化。此后只做构建、发布程序启动和本轮必要的少量交互检查，保留全量脚本待设计稳定后恢复。下表包含此前已经完成的检查；不会因此要求每轮重新执行。

| 检查 | 当前证据 |
| --- | --- |
| Release x64 locked build | 已成功，0 warnings / 0 errors |
| Core / Infrastructure | 428 + 148 = 576 passed，0 skipped；`unit-tests/*.trx` |
| Resources | 初版完整回归见 `resources-retry/resources-verification.json`；最终保留原生单选以支持拖放，并以 `drag-smoke/resources-smoke.json` 验证一次真实拖动及持久化 |
| Dashboard / Activity / AuthenticationDefaults | `native-remaining` 三套已通过；包括默认密钥浏览、保存与导入优先级 |
| Profile / Deletion / Local 与 Remote forwarding | `functional-final/result.json` 通过；使用隔离的真实 SSH 夹具 |
| Transfer | 最终导入布局下 `final-layout/transfer/result.json` 通过：系统文件保存 / 取消、导出范围、JSON / SSH 导入、失败回滚、窄窗、终端入口与重启 |
| 新右键 / Shift+F10 / 侧栏菜单 | `final-layout/uirefresh/result.json` 全部通过，包含主题及 480–1200 DIP 适配 |
| 1000 Servers + 1000 Profiles | `ui-final/resourcescale/result.json`；每页 34 个实际资源按钮，首尾、搜索与取消不写配置通过 |
| 1000 条 SSH 预览 | `final-layout/importscale/result.json`；顶部 / 底部 / 顶部仅 7 / 8 / 7 个可访问实例，窄窗确认区和取消不写盘通过 |
| 格式 / 焦点 | `final-layout/importfocus/result.json`，10 次循环通过 |
| 高对比 / 键盘 | `final-layout/highcontrast`、`final-layout/accessibility` 通过已测范围；高对比恢复为关闭，Flags 126、原方案名均恢复；Narrator 实际语音未测 |

测试驱动器发现的两项环境 / 定位问题：

- 首次拖动的指针偏离目标 6 px，驱动器按原校验拒绝；不放宽校验，重新完整运行 Resources 通过。
- 系统文件选择器的文件夹条目也会使用 AutomationId `2`。原脚本误选文件夹；现仅选择已验证 owner 下、类型为 Button、可见且可用的 Cancel，成功完成导出保存与取消检查。

新菜单测试的旧 CLI 不支持省略 selector 的根窗口属性读取，改为对已知测试 HWND 读取原生物理边界；悬停使用 PerMonitorV2 坐标。原生拖放保留现有 SendInput 驱动及其落点检查。中间尝试的资源测试失败记录保留，不算作通过；最终一次原生拖动 smoke 已通过。

导入滚动时曾将焦点带回格式组，导致格式变化、预览清空；最终将输入区和预览区设为独立的有界区域，并在进入页面时聚焦文件路径。1000 项滚动和反复切换已确认不再触发这个行为。预览初始引导被精简后，相应测试改为检查仍存在的页脚提示及提交可用性。

## 后续默认流程

1. 用 `Build.ps1 -LockedRestore` 构建并启动独立数据实例。
2. 验证本次改动涉及的少量页面 / 交互。
3. 需要修改拖放时可单独用 `Verify-Resources.ps1 -SmokeOnly`；不默认运行全套原生回归、长期压力或重复系统设置测试。

完整脚本仍保留，设计稳定后再逐步恢复。

## 本轮交付

`artifacts/ui-refresh/release-1/TunnelDeck-ui-refresh-1-win-x64.zip`，34,389,243 bytes；解压后 96,936,053 bytes。未签名、仅本地交付，运行库要求沿用 [release.md](release.md)。

SHA-256：`5a2be23a8e666627df722fab3d64b39c23deef42be590bc325ef51c804f8aa55`

已从 ZIP 解压到含空格的路径，校验 91 个文件，并实际启动发布程序；DevWinUI 设置页、Light / Dark 标题栏、统一删除对话框及取消不写配置检查通过。只做本轮轻量发布检查，没有重跑升级 / 回退全套。证据为 `package-smoke/package.json`、`package-smoke/result.json` 和 `user-review/review.json`。

最终独立评测实例使用样例资源和历史，没有启动连接。可用 `Show-UiReview.ps1 -ExecutablePath <解压后的 TunnelDeck.exe>` 重新打开同类隔离评测实例。代码与先前 M10–M13 工作仍未提交。

## 验证边界

用户本次认可此前功能总体可用；新的视觉稿仍需用户评测。此轮不重复无关的长期性能测试。M13 的干净 Windows 安装、真正跨显示器 DPI、200% 系统字号和 Narrator 实际朗读仍按 [M13 验证](m13-validation.md) 单列，不能由本机 UI 截图推定完成。
