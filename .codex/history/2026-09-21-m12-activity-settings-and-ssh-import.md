# M12：活动、统计、设置与 SSH config 导入规则修订

日期：2026-09-21。M11 GUI G 已由用户确认，用户授权继续 M12。M12 实现与自动验证完成，GUI H 待评测；未开始 M13，未提交此前和本轮工作区改动。

## 用户反馈与范围

MobileGym 转发问题由用户确认是服务器参数填写错误，修正后正常。本轮没有修改转发方向或默认监听地址。

用户导入本地 SSH config 时勾选 Skip 后按钮仍灰色，解析器最初得到 0 可用 / 9 不可用。原因是旧规则遇到 Compression / X11 / 转发等不支持选项便跳过整个服务器。用户明确改为：必要的 Host / Username / Port 有效即可导入，其他不支持属性忽略并提示；不回写原 SSH config。

## 实现

- `Core/Presentation/ActivityTimeFormatter`、`RecentActivityItem`：UTC 事件映射到本地相对时间及带 offset 的完整时间，保留历史名称 / 端点 / 错误，标明删除和改名，并区分同名来源。
- `UsageStatisticsProjection`：历史累计总数与当前资源排名分开，过滤已删除 ID 和零次资源，同次数按 ID 稳定选择，显示 Server forwards / Profile uses。
- `DashboardViewModel` / `DashboardPage`：默认 5 条 Recent，按需展开 200 条，列表有高度上限；宽窗并排、窄窗上下。每分钟只刷新时间文字，离页停止 timer，不写配置。
- `TunnelActivityRecorder`：时间转为 UTC；按事件发生时间保留最新 200 条，迟到的旧事件不会挤掉新历史，仍按成功事件累计次数。
- `ConfigurationService.Settings` / `SettingsViewModel` / `SettingsPage`：FIFO 字段更新，原值不写盘、不提高 revision。范围或引用错误不保存，磁盘失败显示固定位置的错误和重试，旧 Quick / Rule 草稿不受默认值修改影响。
- `SshConfigParser`：支持静态字段保留首次取值；未知选项逐项提示忽略。Match 及其条件字段忽略，Include 不读取，命令不执行；只有必要连接字段错误导致条目不可用。可选 IdentityFile 无法表示或缺失时明示密码认证回退，导入后可配置。JSON 导入仍严格全量校验。
- `ImportViewModel` / `ImportPage`：无可导入资源、未预览、预览过期和待勾选跳过都有文字说明。未知可选选项不会再制造不可用 Server。

## 验证与证据

最终通过 **641 项**：Core 414、Infrastructure 142、真实 SSH 85，0 跳过。Release x64 锁定构建 0 warnings / 0 errors。重点新增时间 / DST、历史来源、排名、200 条裁剪、默认值隔离、真实文件重启和保存重试测试。

五套原生验证通过：`Verify-Activity`、`Verify-Transfer`、`Verify-Shell`、`Verify-Forwards`、`Verify-ImportResize`。正式构建连续 20 轮 500×580 / 1200×760 缩放后保持响应。旧里程碑证据不覆盖，结果在 `artifacts/m12-review`，索引见 [M12 evidence](../../docs/evidence/2026-09-21-m12.json)。

使用实际本机 SSH config 的检查仅做只读解析，结果为 9 可导入 / 0 跳过。实际导入提交使用隔离的九条同类样例，验证文件 hash 不变、全部新增资源、无运行活动。正常用户配置、SSH 文件和实际服务均未改动。

初次完整 SSH 回归的恢复组计数测试触发过渡态：Reconnecting 先发布，旧句柄清理后才登记恢复组。测试改为同时等待两项条件，完整 85 项 SSH 重跑通过；生产重连代码不变。原生驱动补充输入前聚焦以及最小导航模式展开 pane。

## 限制与下一步

原生验证中有异常退出，其中一次 WER 指向 Microsoft.UI.Xaml / `E_SURFACE_CONTENTS_LOST`。相同 Transfer 流程重跑通过，带临时异常记录的多次启动 / 缩放也通过；临时诊断代码已移除，最终正式构建的 20 轮缩放通过。未稳定复现及确定根因，不声称已修复，M13 应优先跟进。没有通过吞掉未处理异常掩盖退出。

用户尚未确认 GUI H。`Show-ActivityReview.ps1` 提供独立样例窗口；用户评测辅助区、设置与新的导入提示后，再推进 M13。系统 High Contrast、全面 DPI / 无障碍 / 性能与干净机器发布仍在 M13，未用本轮结果替代完整首版验收。
