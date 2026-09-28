# 脚本目录整理

## 目的与评估

用户希望评估 `scripts/` 中的非日常脚本并保持一级目录干净。53 个脚本中，6 个为日常入口或其共享环境初始化，47 个用于调试、回归、性能测量及人工评测。后者不参与应用运行，但辅助脚本有调用依赖，发布流程的 `-Verify` 也依赖发布验证，因此集中保留而未删除。子目录使用常见拼写 `scripts/debug/`。

## 改动

- 一级保留 `Build.ps1`、`Run.ps1`、`Test.ps1`、`Publish-Release.ps1`、`Clean-GeneratedArtifacts.ps1`、`Enter-DevelopmentShell.ps1`。
- 26 个 Verify、5 个 Show、3 个 Measure 和 13 个辅助脚本移入 `debug/`，原文件名与参数保持一致。
- 调整迁移脚本中的仓库根目录计算，以及共享 Run / Developer Shell、删除场景 SSH 夹具和原生调试符号缓存路径。输出仍进入原仓库根目录的 `.test-output/`、`artifacts/`。
- 发布入口改为调用 `debug/Verify-Release.ps1`。构建、测试、普通启动和清理入口内容保持原样。
- 新增 `scripts/README.md` 与 `scripts/debug/README.md`；将项目 README 的详细 UI 命令目录移至调试说明，更新当前文档及 handoff 的具体路径。历史 evidence / history 保留原始记录。

## 验证

- 对 53 个脚本逐一核对迁移前文本与应用预期路径替换后的 SHA-256：全部匹配，无脚本丢失或其他代码变更。
- PowerShell AST 解析 53 个脚本，零语法错误。
- 检查 136 处静态脚本 / 项目依赖、34 处迁移后的仓库根目录计算，全部通过。
- 验证原生回归聚合器的 13 个动态套件路径及 `OutputDirectory` 参数，全部可解析。
- 检查当前文档中的 109 处具体脚本引用，全部存在。
- 本次为目录和引用调整，未执行应用构建、GUI / SSH 回归或重新发布包。

## 既有边界

部分历史脚本仍依赖旧 UI 文案、布局或配置版本，例如 `Verify-Storage.ps1` 将 schema 3 当作未来版本；目录迁移不表示这些历史场景已适配最新多语言版本。相关说明已加入脚本文档，本次不扩展为回归脚本重写。

应用代码、用户数据、SSH 配置和 `.codex/prompt/260919.md` 未修改；此前工作区改动保留，未暂存或提交。
