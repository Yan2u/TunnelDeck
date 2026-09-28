# M11：导入导出与独立终端

2026-09-21，续接用户指定的两个会话。前一会话已确认 M10 GUI F 通过；保留 M10 收尾的工作区改动，并完成 M11。当前基线为 8665720da5a09bbffa99b1a1b2a11894cd287d9e，未创建提交。

## 内容与目的

- Core/Transfer：独立的 TunnelDeckExport v1；Profiles 附带全部 Server 依赖及 Pinned / Other 顺序；JSON 全量校验、所有 ID 与引用重新映射，避免重名覆盖及裸 ServerId。
- ResourceImportService：复用 ConfigurationService FIFO 原子事务和 ServerWorkRegistry 准入；预览后删除拒绝陈旧提交，导入改变 Delete all 影响后要求重新确认。只追加新身份，不干扰原有转发 / 重连。
- SshConfigParser：静态 Host / HostName / User / Port / 单一 IdentityFile、首次取值、引号与注释；无法表示的字段明确列出行号。Include / Match / ProxyCommand 不执行。SSH 部分导入需明确勾选跳过；非法 JSON 不做部分导入。
- Infrastructure/Transfer：严格 UTF-8 编解码、大小与 schema 限制、原子导出、错误重试；文件任务纳入有界退出，取消前不截断已有文件，发布后不误报失败。
- App：ImportPage / ExportPage、All 页 More 入口、原生文件选择器、独立滚动预览及固定底部动作、警告结果；Quick / Server 详情的 Open terminal，未保存身份不可启动，设计预览禁用。
- Show-TransferReview.ps1 可重复创建隔离 GUI 评测窗口与 JSON / SSH config 样例；只使用标有 M11 review 的资源及保留的 .example 主机名。
- Infrastructure/Terminal：固定 OpenSSH / Windows Terminal 适配，ArgumentList 与 -F none；分号 / 引号参数走直接 SSH 控制台。外部终端不获得应用秘密或信任，退出与转发相互独立。

## 验证

- Release x64 锁定构建：0 warnings / 0 errors。
- Core 387、Infrastructure 136、真实 SSH 83，共 606 项通过、0 跳过，相对 M10 增加 94 项。
- 真实 argv 接收程序验证空格、Unicode、引号、分号、反斜杠和选项注入形态；实际 Windows Terminal 路径通过。OpenSSH 缺失、终端缺失 / 创建失败、密钥缺失及取消均覆盖。
- 真实 SSH 覆盖跨服务器 Profile 往返后的 Local / Remote payload、原转发保持运行、重连期间导入 / 删除，以及外部终端两侧独立关闭和不复用应用信任。
- Verify-Transfer.ps1 的 11 组原生检查通过；Shell、Resources、Connections、Forwards、Lifecycle 五套回归通过。新增 OutputDirectory 参数，把回归证据保存到 M11 目录，保留历史截图。

## 验证中确认的边界

系统文件选择器在 broker 进程中运行，用户截图确认其可见；仅按应用 PID 枚举会漏掉它。测试改为按 owner 找 HWND，并在 UIA 设置文件名后发送真实编辑事件以提交 Shell 路径。

本机 Windows ssh-keyscan 9.5 的 sntrup KEX 不兼容夹具。真实终端测试改用按夹具指纹验证的 SSH 连接获取公钥，写独立 known_hosts；实际 ssh.exe 的认证和交互验证通过，产品不依赖 keyscan。

## 状态与下一步

契约见 docs/transfer.md、docs/terminal.md，证据见 docs/evidence/2026-09-21-m11.json，截图 / 报告在 artifacts/m11-review。

M10 GUI F 保持已验收。用户本轮的保存对话框截图只确认 picker 可见，M11 GUI G 仍待完整评测。用户确认后再推进 M12；完整性能 / DPI / 无障碍与发布仍属 M13，不宣称首版整体完成。
