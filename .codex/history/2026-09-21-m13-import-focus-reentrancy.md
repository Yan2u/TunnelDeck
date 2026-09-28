# M13：定位并移除导入预览的焦点重入

继续用户已授权的 M13。M0–M12 / GUI A–H 已确认。本文不将综合验收标为完成。

## 原始异常证据

关闭 RTSS 后的 PID 49444 仍在导入页缩至 500×580 后异常退出。`0xc000027b` 包含 `0x802b0020`，仅按系统 HRESULT 名称将其称为 `E_SURFACE_CONTENTS_LOST` 不足以诊断本次故障。

使用微软 Windows SDK x64 CDB 与匹配的 Microsoft.UI.Xaml 3.2.3.0 PDB 分析原始 WER minidump。原生堆栈处于 `CFocusManager::RaiseAndProcessGettingAndLosingFocusEvents`。从 stowed exception 的 LEO1 对象，经 SOS `dumpccw` 找到托管 `COMException`，还原的实际消息为：

> Focus cannot be moved while getting or losing focus.

异常堆栈为 `RadioButtons.SelectedIndex` 双向绑定 → `ImportViewModel.SourceIndex` → `Invalidate` → `RefreshPlan` → `IItemsControlMethods.set_ItemsSource`。格式控件放在 ListView.Header 内，其获取焦点回调同步重置父列表数据，又触发列表的焦点迁移。

原始解析日志位于 `artifacts/m13-review/crash-analysis/49444-symbols.log`、`49444-managed-exception.log`、`49444-error-text.log`。最后一份以 UTF-16 数值还原消息，避免 CDB 控制台编码丢失中文和英文异常文本。未将用户转储上传到外部服务。

## 修改

- `ImportPage.xaml`：有界 ScrollViewer 内依次放输入表单和采用 StackLayout 的 ItemsRepeater。表单不再属于可重置列表的 Header；只读预览保持虚拟化，底部动作仍固定。
- `ReadOnlyListItem`：给 ItemsRepeater 的只读内容补充有名称的 ListItem automation peer，保留文本子节点，不添加无动作的 Tab 停靠点。
- `ImportViewModel.SourceIndex`：仅接受有效的两种格式，忽略控件生命周期中的无选择索引。
- `Verify-ImportFocus.ps1`：检查缩窗、滚动、重新获取格式控件焦点、切换格式使旧预览失效，以及重新预览。
- `Verify-ImportScale.ps1`：1000 条静态 SSH 条目的首尾可达、实际 UIA 名称节点数量、窄窗按钮和取消不写盘。
- `Verify-Transfer.ps1`：支持额外缩窗轮次；保留单轮的原始交互路径。
- `Native-DebuggerSupport.ps1`：可选 CDB，仅附加显式隔离测试进程，异常时保留完整转储，不更改产品异常处理。符号使用本地缓存，避免初始断点被网络符号下载阻塞。
- `Lifecycle-TestSupport.ps1`：更新为 schema 2，使生命周期测试继续检查没有存储故障提示；真实升级测试保留 schema 1。
- `.gitignore`：明确纳入不含凭据的 `LocalFolder.pubxml` 发布配置，避免被通用 `*.pubxml` 规则遗漏。

未压制异常、限制最小窗体或修改图形叠加层配置。较早将 RTSS 关闭视为充分规避条件的判断已撤回。

## 验证状态

Release x64 锁定构建通过，零警告、零错误。修改前新增的焦点用例 10 轮通过，说明它不是对历史间歇故障的确定性复现；判断原因依赖原始失败转储。修改后的完整 Transfer、焦点循环、1000 条预览、显示和文字缩放检查继续记录到 `docs/m13-validation.md`，不以挑选一次绿色运行宣称消除了所有 WinUI 问题。
