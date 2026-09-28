# M13：键盘与本地交付候选

M12 / GUI H 已确认，本轮继续 M13，保留全部 M10–M13 未提交改动。首版未标为最终完成。

## 界面收尾

- 新建 Server / Profile 原先按 Enter 导航后，焦点回到标题栏。`ShellPage.FocusInitialControl` 等待页面 Loaded 并排队到当前焦点事件结束后，把新表单焦点交给名称输入框；只在新建导航时请求，不在后台刷新时抢焦点，离开页面后的请求失效。
- Server 的 Display name / Host / Port / Username 错误增加稳定 AutomationId 与 Polite 标记。`Verify-Accessibility.ps1` 验证新建焦点、Tab / Shift+Tab、Enter / Esc、错误名称、只读预览行与确认区键盘操作。
- `ReadOnlyListItem` 为 ItemsRepeater 提供命名 ListItem peer 和文本子节点，不为纯展示行增加 Tab 停靠点。1000 条预览顶部 / 底部 / 顶部实际只暴露 9 / 11 / 9 行，取消不写盘。测试按 ListItem 类型选择，避免同名行与 TextBlock 导致 wait-for 歧义。
- 修正后的导入页通过真实 100 / 150 / 200% DPI、高对比与反复缩窗检查。显示恢复 125%；高对比恢复关闭，Flags 126 与原方案名保持一致。

## 发布

`LocalFolder.pubxml` 固定 x64、框架依赖、多文件发布；`.gitignore` 明确保留这个不含凭据的配置。`PublishXamlResources.targets` 将 PRI / XBF 加入发布文件，修复默认 publish 遗漏 WinUI 资源而不能启动的问题。没有修改 NuGet 依赖版本。

最终候选为 `artifacts/m13-review/rc-final/TunnelDeck-m13-win-x64.zip`，31,584,908 bytes，解压 88,094,994 bytes，SHA-256 `03183beb75196d75302a8933c853e3bfd183bb04c01ce02574ddcdc730dd9c80`。未签名、没有外部发布。随包提供运行库检查、README 与 Windows PowerShell 5.1 隔离 smoke。

`rc-final-smoke` 从最终 ZIP 解压并校验所有文件，再验证真实 UI 从 schema 1 升级到 2、保留旧备份、重启不改写、真实旧 exe 保护新 schema、独立旧备份回退、无自动连接。`packaged-accessibility` 直接验证发布目录中的程序，全部所测键盘与导入行为通过。

`Show-FinalReview.ps1` 为用户打开发布程序的独立评测实例，使用 .example 样例与演示历史，不触碰正常数据目录。

## 未完成项

200% 系统文字的自动化提交没有真正改变 Windows / 应用字号，失败记录保留，100% 与原注册表状态已恢复。Narrator 实际语音与 LiveRegion 播报未验证；winapp 对 LiveSetting 返回 null，不能据此报通过。只有一块显示器，没有干净 Windows / Sandbox 环境。最终用户 GUI 评测也仍待确认。

当前正式汇总为 `docs/m13-validation.md`，性能口径为 `docs/performance.md`，不可把开发机通过替代上述环境验收。
