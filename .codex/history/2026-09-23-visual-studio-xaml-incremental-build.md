# 修复 Visual Studio XAML 增量构建错误

用户报告 Visual Studio 构建在 `RuleEditorView.g.cs`、`ProfileDetailPage.g.cs` 中找不到已移除的 `OnApplyClick`、`RuleEditor` 等成员。

## 原因与修复

- 使用指定 Visual Studio 开发环境中的 `MSBuild.exe` 对 `Debug | x64` 执行普通 Build，复现全部 8 个 CS1061 错误。
- 现有 Debug 目录中，设计时 `.g.i.cs` 已更新到规则对话框版本，但 `.g.cs` 仍包含页面内嵌规则表单的事件与字段引用；两者首行 XAML checksum 不一致。`XamlSaveStateFile.xml` 还保留迁移前的本地程序集路径。此前发布使用独立的 `Release/.../win-x64` 中间目录，因此没有覆盖这条失败的构建路径。
- 新增 `src/TunnelDeck/Build/XamlIncrementalBuild.targets`，由 UI csproj 导入。实际构建独立记录 XAML、项目文件和该 targets 文件的完整路径及 SHA-256；只有输入集合或内容变化时，才在 MarkupCompilePass1 前删除当前配置的 WinUI saved state，让生成器更新两个阶段的代码。
- 完成实际 MarkupCompilePass2 后记录成功标记，设计时构建不推进此标记。输入未变时跳过失效步骤；输入清单及标记加入 FileWrites，随正常 Clean 管理。
- 没有手工修改生成代码、补回已删除的 UI 成员，也没有清理 NuGet 缓存或整棵 obj 目录。现有规则对话框及功能不变。
- README 增补 Visual Studio 构建入口和增量缓存约定。

## 验证范围

仅执行此次故障相关的 MSBuild 构建验证；不运行 UI 或 SSH 全量回归。日志保存在 `artifacts/vs-xaml-fix`。

## 最终验证结果

- 使用指定 Visual Studio 开发环境中的原生 `MSBuild.exe` 验证。修复前的 8 个 CS1061 错误记录于 `debug-before.log`；首次修复后的普通 Debug / Release x64 构建均为 0 警告、0 错误（`debug-after.log`、`release-after.log`）。两处 Debug 生成文件的 `.g.i.cs` / `.g.cs` XAML checksum 一致，Debug / Release 生成代码中均不再包含已移除的成员引用。
- 增量检查中发现显式 `Encoding="UTF-8"` 会使 Desktop MSBuild 重写内容相同的带 BOM 清单。移除该属性，保留默认无 BOM 写入；连续执行输入收集后，清单时间戳保持不变。
- 最终 Debug x64 构建通过，0 警告、0 错误（`debug-isolated-final.log`）。使用 `.test-output/vs-xaml-build/Debug/` 作为独立输出目录，仍使用原有 Debug 中间目录。
- 随后以相同参数再次构建通过，0 警告、0 错误（`debug-incremental-final.log`）。详细日志明确记录 `InvalidateStaleTunnelDeckXamlState` 因输出相对于输入已是最新而被跳过，确认输入未变时不会反复使缓存失效。
- 最终普通 Release x64 构建通过，0 警告、0 错误（`release-final.log`）。
- `debug-final.log` 保留了一次输出复制失败：用户启动的 Debug 实例锁定 `TunnelDeck.exe`，XAML / C# 编译已通过，随后复制步骤报告 MSB3027 / MSB3021。未结束用户进程，改用上述独立输出目录完成验证。用户在 Visual Studio 中再次构建原输出目录前，应停止调试或关闭该实例以释放可执行文件。
