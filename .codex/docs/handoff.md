# TunnelDeck 新会话交接

本目录是规划资料，不是 TunnelDeck App 源码。用户已经创建空白 WinUI 3 template，但当前规划会话工作区为 `C:\Users\19559\Mp\Python\tools`，未读取或构建那个模板。应在真实模板项目中开始下一次实现。

## 文档阅读顺序

1. [implementation-plan.md](implementation-plan.md)：目标、架构、数据契约、M0–M13 实施/测试/验收。
2. [research-and-decisions.md](research-and-decisions.md)：经官方文档核实的边界及尚未由澄清回复确认的规划默认。
3. [ui-ux-spec.md](ui-ux-spec.md)：用户原始 77 条 UI/UX 规范，仅去掉引用符号，作为视觉和交互基准。

如将此目录复制进 TunnelDeck repo，应整体复制以保留相对链接；源目录绝对路径为：

`C:\Users\19559\Mp\Python\tools\docs\tunneldeck`

## 可以粘贴给新会话的请求

```text
请基于当前 TunnelDeck 空白 WinUI 3 template 开始实现。

先阅读 C:\Users\19559\Mp\Python\tools\docs\tunneldeck 中的
implementation-plan.md、research-and-decisions.md、ui-ux-spec.md，
以及本项目适用的 AGENTS.md。整体目标是“构建 TunnelDeck - 一个 WinUI3 的端口转发助手”。

本轮先完成 M0 和 M1：核对并运行现有模板，建立测试环境，验证 SSH.NET 的
Local/Remote 转发、单条停止、远端拒绝/超时语义、host key 校验和 WinUI 基础窗口能力。
保留现有模板启动/打包模式，不重建一个重复项目，不直接铺开全部页面。

此前未确认的四项规划默认是：
1. 关闭窗口停止所有转发并退出；
2. SSH.NET 直连、密码和私钥、有限 SSH config 导入；
3. 可确认的远端监听拒绝有限顺序重试，但不武断提示为端口占用；
4. 首版隐藏累计流量，仅做有可靠口径的基本统计。
如果本任务中有我对此作出的更新答复，以更新答复为准。不要把规划默认写成我已确认。

非 GUI 行为由你编写并运行自动化测试，真实 SSH 测试使用隔离夹具。
GUI 变更先 build/run，确认实际窗口，再按对应里程碑给出简短人工评测步骤和通过标准。
不要把尚未获得我的评测标成通过；等待 GUI 反馈期间可以推进无依赖的底层工作。

每次较大改动在 .codex/history 写记录。完成 M0/M1 时交付实际命令、测试证据、
已确认技术边界和阻塞项；后续再按 M2–M13 的依赖逐层推进。
```

## 实施时不能丢失的约束

- Profile 可跨多个 Server；运行实例和配置分离，Requested 与 Actual 分开。
- 同 Server session 共享，控制请求串行；实际网络数据链路要测试。
- 本地自动搜索只调整本地监听端，远程自动搜索只调整远端监听端。
- SSH `-R` 失败原因不透明；超时可能有副作用，不能一律当端口占用。
- SSH.NET 信任必须显式校验，密码和私钥口令不进 JSON/日志/导出。
- Sidebar 是有限 working set；All 的 Other 顺序不随 Recent 更新而变。
- 删除 Server 先算影响，全部停止成功后才能提交配置删除；失败保留配置，已经停止的运行实例如实显示。
- 原生 WinUI 默认、ThemeResource、Mica、紧凑布局优先；不采纳通用 Web 技能生成的视觉风格。
- 正常关闭清理与异常网络下远端释放的保证不同，不作无法证明的承诺。

## 当前完成情况

已完成：规划、原始规范归档、官方资料核实、里程碑测试/验收设计。

尚未执行：模板审计、App 代码、NuGet 安装、SSH 实验、编译、GUI 验收、打包。新会话不得将本文档交付误当成 M0 或 M1 已通过。
