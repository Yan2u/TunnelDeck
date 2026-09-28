# M12 反馈：默认认证方式与私钥文件

日期：2026-09-21。按用户测试反馈继续完善 M12；GUI H 待用户评测，未进入 M13，未提交工作区已有改动。

## 需求与行为

代码检查确认标准 SSH `IdentityFile` 原本已支持：单个可解析且存在的文件会使导入的 Server 使用私钥认证。本轮保留并验证该行为，补上全局默认认证及默认密钥，减少导入后逐台修改。

- Settings → Server authentication 默认 Password；选择 Private key 后才显示密钥项。自动检测 `%USERPROFILE%\.ssh\id_rsa`，仅检查文件存在，不读私钥内容。可浏览其他文件或手输完整路径；浏览即时保存，手输用 Enter / Save path 保存，留空恢复自动检测。
- 新 Server 草稿和无 `IdentityFile` 的 SSH config 条目使用默认值。有效的显式 IdentityFile 优先；已有 Server、旧草稿、JSON 中的显式认证均不被默认值改写。
- 密码模式保留已配置的默认密钥以便切回，但不复制到新的密码 Server。选择默认密钥却没有可用文件时，导入要求先修正设置；不会静默降为密码。显式 none / 多路径 / 无效 IdentityFile 沿用前轮约定的忽略提示及密码回退，不用全局密钥替代显式声明。
- 无效手输路径不覆盖已保存值；磁盘失败可 Retry。导入预览在设置修改后失效，提交重新检查密钥文件，原 SSH config 不回写。

## 涉及模块

- `Core/Models/AppSettings`、`Policies/ServerDefaultsResolver` 与 `Validation/ConfigurationValidator`：默认值、id_rsa 检测、新草稿预填和路径 / 认证枚举校验。
- `Core/Transfer/SshConfigParser`：显式与默认来源的优先级及预览说明；`ServerDetailViewModel.Load` 仅为新 Server 应用默认值。
- `SettingsViewModel`、`SettingsPage.xaml`、新增 `SettingsPage.Authentication.cs`：条件显示、原生文件选择、路径编辑 / 保存 / 重试，页面事件与设置模型分离。
- `AppState`、`AppStateJsonCodec`、新增 `AuthenticationDefaultsMigration`：内部 schema 1 → 2。严格验证旧 Settings 四个必需字段及未知字段后补 Password / null；加载仅内存迁移，首次实际编辑才保存新格式，旧版程序看到新版本保持只读。导出、主机信任与凭据清理格式仍是 v1。
- 新增认证默认值 Core / Persistence / SSH 测试，以及 `Verify-AuthenticationDefaults.ps1` / `Show-AuthenticationReview.ps1`。共享 picker 驱动根据已确认所有权的 HWND 选择唯一可见的 Edit，兼容 Open / Save 的不同 ID；取消后等待对话框关闭再退出应用，保留前台保护。

## 验证

**663 项通过，0 跳过**：Core 428、Infrastructure 148、真实 SSH 87。覆盖默认检测、显式优先、密码默认、缺失密钥、草稿隔离、JSON 认证不变、预览过期、旧格式迁移与旧版保护。两条新增真实 SSH 用例分别通过默认与显式密钥认证并验证转发 payload。

Release x64 锁定构建：**0 warnings / 0 errors**。证据保存到 `artifacts/m12-auth-defaults`，不覆盖原 M12 的 641 项历史结果。

`Verify-AuthenticationDefaults.ps1` 八组原生检查通过，包括实际 id_rsa 检测、文件选择 / 取消、Unicode 路径、新 Server 保存、SSH 默认 / 显式导入、密码模式隐藏字段、无效路径 / 磁盘失败 / Retry、500 epx 深色布局和重启。`Verify-Storage.ps1` 通过未来版本只读、备份恢复、外部修复和干净加载不写盘；其当前格式夹具更新为 schema 2，未来版本为 3。

新原生脚本调试期间发现两个驱动问题并修正：Add 按钮动态 ID 为 AddServer；ComboBox 保存后可能替换 popup peer，改用原生 Home / End 并等待保存。未因此修改产品行为。

完整 `Verify-Transfer.ps1` **两次失败**：前六组导出 / 选择器 / 预览 / 原子失败重试检查通过，随后在 500×580 缩窗后滚动 SSH 预览时退出。WER 记录与既有 Microsoft.UI.Xaml / `E_SURFACE_CONTENTS_LOST`（0x802b0020）一致。保留两份失败结果和事件记录，未加入异常压制，也不宣称完整 Transfer 回归通过。M12 初版曾通过的 20 轮缩放仅是历史证据；该问题仍需 M13 优先调查。

## 交付与下一步

索引见 [本轮证据](../../docs/evidence/2026-09-21-m12-authentication-defaults.json)。README、handoff、milestones、实现计划和 UI 规范已同步默认认证及验证状态。

`Show-AuthenticationReview.ps1` 提供独立数据目录、样例活动及 .example 服务器，直接展示认证设置。只在评测数据中预选 Private key；普通用户配置、实际服务器与本地 SSH config 未改动。用户继续评测 GUI H 后再推进 M13；已知渲染异常不以本轮认证功能通过替代验收。
