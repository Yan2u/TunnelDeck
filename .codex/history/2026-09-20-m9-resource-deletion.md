# M9：资源删除、影响确认与失败恢复

日期：2026-09-20。延续任务 `01a0b7ed-02e4-7680-bc94-e9b17e9273af` 中未交付的 M9 工作。已记录用户「GUI 测试通过，继续 M9.」为 M8 GUI D 通过，GUI A / B / C 保持此前确认；M9 GUI E 待评测。保留 `codex/m0-m1-foundation` 的 M0–M8 未提交工作区及用户 `.codex/prompt/260919.md` 修改。

## 目的与实现

- 新增 `Core/Deletion`：统一单个 / 集合 / Delete all 的 ID 影响扫描、去重计数、快照比较和删除协调，覆盖重名、跨服务器规则、Quick、启动中的实例、等待凭据及连接测试。
- 新增 `Core/Operations/ServerWorkRegistry` 与 lease / barrier。Quick、Profile 和连接测试在等待输入前登记；Server 删除禁止新工作并排空已接受任务，其他服务器继续运行。Profile 删除仅阻止新的启动，已接受快照继续。
- `SshConnectionManager.Deletion` 按固定 ID 顺序取得身份门禁并清理连接；`ConfigurationService.Deletion` 在 FIFO 事务中重算影响、停止后构造完整候选并原子提交。停止或保存失败保留整份配置，成功后清理引用与无主凭据，共享凭据和持久补偿沿用 M3。
- 新增 `DeletionConfirmationModel`、原生 `DeletionDialog` 与 `DialogService.Deletion`；列表 / 详情 More 接入单个和全量删除。Shell 提供进行中状态、已停止数量、Review and retry，以及独立的凭据清理重试。
- 默认焦点为 Cancel，Enter / Esc 取消。单服务器展示 endpoint、短 ID 和按 Profile 分组的端点；零项隐藏、长列表折叠 / 滚动、全量只显示摘要，主题危险颜色与原生控件保持一致。
- 配置更新后清理默认 / Quick / 顺序 / Sidebar / 搜索 / 导航。保留空 Profile 和历史快照；Profile 删除后已运行隧道 Details 保留来源文字，并移除失效导航。

## 本次续接发现并修复

实际窗口检查发现，删除确认仍显示时关闭窗口会在 Window.Closed 后执行延迟 Hide，导致原生崩溃。`DialogService.CloseAsync` 现在在窗口存活时取消并排空对话框，完成后才销毁窗口；延迟回调检查是否已结束 / 已释放，DeletionDialog 也成对管理 XamlRoot 订阅。原生关闭确认、重启后配置未变已验证。

写入故障后重试原先仅移除本次停止的行，上一轮已停止的行会残留。扩展已有失败提交测试先复现，再调整成功提交后的清理：仅对已删除 Server 的全部终态行操作，并且必须先成功刷新 Activity。验证停止历史保留且 TotalForwards 不重复计数。

为 Details 的来源和导航补充稳定 AutomationId。Quick 键盘回归的驱动改为先激活目标窗口、确认具体输入有焦点再按 Enter；连接表单测试使用 AllServers 的原生 Invoke 导航。原始端口输入逻辑保持 M7 已验收版本。

## 验证与交付

Release x64 锁定还原构建 **0 warnings / 0 errors**。Core **304**、Infrastructure **92**、真实 SSH **63**，合计 **459 / 459 通过，0 跳过**。相对 M8 新增 29 项用例，覆盖影响变化、停止 / 保存失败、迟到监听、并发命令、凭据清理与真实释放。

新增 `Verify-Deletions.ps1`，拆出 `Deletion-TestSupport.ps1` 与 `Deletion-ForwardingChecks.ps1`。14 组删除 UI 检查通过，包括 4 条真实 Profile / Quick 转发、删除 Profile 后 payload 保留、锁文件下停止但旧配置不变、重试 / 选择清理 / 空 Profile、全量释放、历史与重启，以及 500 epx 的长清单和固定按钮。原有功能的最终回归结果、日志、截图、源码 / 产物 / TRX 散列统一记录于 `docs/evidence/2026-09-20-m9.json`。

最终九套原生检查全部通过：Deletions、Profiles、Forwards、Connections、Resources、Shell、Window、Dashboard、Storage。资源拖排曾被实时字幕窗口切换前台中断；保留保护并重跑通过，没有关闭用户的辅助功能或绕过输入保护。

新增 `docs/deletion.md`，同步里程碑、README、Profiles / Quick / Shell / 存储 / 引擎契约和 `.codex/docs` 规划、规格及交接。GUI E 继续保留待用户评测，不将自动化当作人工确认。所有故障删除均作用于隔离数据与 SSH 夹具，Agent 未切换系统 High Contrast。

后续为 M10 自动重连、完整退出恢复与单实例；M11 导入和 M10 重连须经过共享门禁，不得复活已删除的资源或使用陈旧快照。本轮未扩展远端失败自动递增，也未实现整个首版的发布验收。
