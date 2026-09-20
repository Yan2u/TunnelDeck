# M9 资源删除与一致性

更新：2026-09-20。M8 GUI D 已由用户「GUI 测试通过，继续 M9.」确认。M9 的实际构建、自动化证据及 GUI E 状态见 [milestones](milestones.md)。

## 确认与影响

`ResourceDeletionService` 提供单个 / 集合 Server、Profile 和 Delete all 的影响计算及确认后执行入口。`DeletionImpactCalculator` 只按稳定 ID 扫描，单个和全量共用算法；重名不影响身份，跨服务器 Profile 的数量去重。影响保留配置、运行与待启动工作的 revision，以及受影响的服务器、规则、来源和运行快照。

Server 删除包括 Quick Forward、Profile 的 Starting / Running / Stopping / Reconnecting 实例、等待凭据或排队的启动工作，以及连接测试。已绑定到运行实例的待启动规则不会在两类数量中重复计算。Profile 删除仅影响配置，已接受的启动和现有转发继续使用来源快照。

All 页面和详情页的 Delete / Delete all 均置于原生 More 菜单，使用主题危险颜色。原生 ContentDialog 默认聚焦 Cancel；Enter 默认取消，Esc 取消。单 Server 展示 endpoint、短 ID、按 Profile 分组的规则；空影响区隐藏，大于 8 条规则时先折叠详情，展开后在对话框内滚动，操作按钮不随正文移动。Delete all 只显示摘要，不列每条规则。删除 Profile 明确说明保留 Server 和现有转发，详情页也提示丢弃未保存草稿。

## 执行顺序与并发

1. 对删除操作串行调度，在共享 `ServerWorkRegistry` 设置范围门禁，再按 ID 固定顺序取得服务器身份门禁。
2. 进入 `ConfigurationService` 的事务队列和文件锁，重新计算影响；相关资源、规则、运行代次 / 状态或待处理工作变更时返回 `NeedsConfirmation`，先释放门禁再重新显示确认。无关使用计数和时间戳不使确认失效。
3. Server 删除取消并等待相关工作实际退出，再停止全部相关隧道和 SSH 连接。迟到监听仍由原任务追踪并清理，不会只取消外层等待。其他 Server 的 Profile 规则可以继续。
4. 所有停止成功后构造候选状态：删除 Server 和引用规则，清理 DefaultServerId、PinnedQuickServerId、Pinned / Other；保留变空的 Profile。
5. 原子提交成功后发布配置，由 Quick、Sidebar、搜索和导航统一校正失效引用。独占凭据沿用 M3 的持久清理队列，共享凭据在仍有引用时保留。
6. 离散活动写入排在删除事务之后，不能在持有配置队列时等待它们。停止历史保存成功后才清除相应活动行；已提交删除不因后续活动写入失败被误报为失败。

`ServerWorkRegistry.Begin` 在 Quick 输入、Profile 批次和连接测试之前登记工作，已接受的操作持有 lease；门禁生效后拒绝新工作，成功删除留下 ID 墓碑，旧快照不能重新启动。未来 M10 的重连和 M11 的导入必须复用此门禁与事务顺序，不得自行恢复已删除引用。

## 失败与取消

用户取消确认没有停止、保存或凭据清理副作用。确认期间影响变化只更新清单，重新确认前不停止任何工作。

任一监听 / 连接清理失败，整个配置删除中止，服务器、规则、默认值均保留。已经停止的实例如实显示终态，清理未完成的实例保留所有权与 Stopping；不自动重启来模拟运行回滚。

磁盘提交失败时内存和旧 JSON 都保留；InfoBar 说明已停止的数量，并提供 **Review and retry**。重试重新计算影响并再次确认，不复活停止的实例。若原子文件已经发布，即使后续文件操作报告异常，仍依据已发布内容判定提交成功。配置删除成功但凭据清理失败时单独提供 **Retry credential cleanup**，无需重新删除配置。

删除 Profile / Rule 不停止已接受或已运行的转发，也不删除服务器；历史和 Details 保留来源名称，删除的 Profile 不再提供导航入口。Rule Remove 仍是可丢弃的草稿操作，Save 后才更新配置。

## 窗口关闭

`AppServices` 的退出顺序先排空 Deletion，再按 Profiles → Forwarding → Tunnels → Connections → Activity → Configuration 清理。窗口销毁前调用 `DialogService.CloseAsync`，取消并等待已显示 / 排队的确认对话框；延迟取消回调不会对已关闭窗口调用原生 Hide。删除期间关闭会保留未提交配置，并等待已经接受的监听清理。

网络黑洞或进程异常结束时，远端端口的释放仍取决于 sshd 的连接失效检测；M9 不改变 [转发契约](tunnels.md) 中的这个边界。M10 的自动重连、单实例与完整关闭恢复尚未实施。

## 验证

```powershell
./scripts/Build.ps1 -LockedRestore
./scripts/Test.ps1 -Ssh -NoFixtureBuild
./scripts/Verify-Deletions.ps1 -NoFixtureBuild
```

`Verify-Deletions.ps1 -LayoutOnly` 可单独运行无网络的界面场景。完整脚本使用隔离配置、独立 OpenSSH 夹具及一次性凭据；不删除用户的服务器。原生输入脚本顺序运行，复用前台 / 坐标保护。GUI E 由用户评测，不能用自动化结果代替。

| 计划用例 | 自动化覆盖 |
| --- | --- |
| DELETE-01 | 重名、跨服务器去重、零影响、1000 条规则，原生分组 / 全量摘要与长清单滚动 |
| DELETE-02 | 取消无写入 / 停止，确认期间新增规则、运行实例或全量资源时重新确认 |
| DELETE-03 | 第二个句柄清理失败保留全部配置，第一个已停止状态可见，后续重试清理 |
| DELETE-04 | Stop 后提交失败、真实文件故障、提交后异常、共享凭据与持久清理重试队列 |
| DELETE-05 | 单个 / 全量删除引用完整性、空 Profile、默认 / Quick / 排序 / 搜索 / 导航与重启 |
| DELETE-06 | Profile 删除保留真实 Local / Remote payload、来源可读、已删除来源不可导航 |
| DELETE-07 | 等凭据、连接测试、迟到真实 Remote 确认、启动门禁、排队保存与关闭交错；重连 / 导入的完整场景留待对应里程碑 |

GUI E 重点：单个与全量的确认信息易懂；Cancel 默认、长清单和窄窗可用；危险命令在 More；删除失败后的停止数量与重试入口清楚。
