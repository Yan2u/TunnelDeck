# Open Terminal

Quick Forward 的 More 与已保存 Server 详情的 More 提供 Open terminal。详情中未保存的连接参数变更会禁用该命令；保存后使用最新的已保存身份。设计预览禁止启动外部连接。

终端是用户拥有的独立 SSH 会话：使用 OpenSSH 的认证交互与 known_hosts，不读取 TunnelDeck 的密码 / 私钥口令或 SSH.NET 信任库。打开进程不表示认证成功。退出 TunnelDeck 不关闭终端，关闭终端不停止应用里的转发。

## 启动适配

优先使用当前用户 WindowsApps 中的 wt.exe，打开新窗口、新标签，并运行系统 OpenSSH Client。Windows Terminal 不存在或创建失败时直接启动控制台 ssh.exe；系统 OpenSSH Client 不存在时提示从 Windows Optional Features 安装。

固定参数通过 ProcessStartInfo.ArgumentList 传递，包括：

- -F none：不再读取用户或系统 SSH config，防止已解析的显式 Host / User / Port 被另一份配置改变或执行 Match / ProxyCommand。
- -p 与 -l：独立参数传 SSH 端口、用户名。
- 私钥认证时传 -i 和 IdentitiesOnly=yes；路径必须是存在的绝对文件路径。
- -- 后传规范化主机名 / IP；DNS 国际化名称按 IDN 规范化。

不拼接 cmd、PowerShell 或用户 shell 模板；不将秘密写入参数、环境变量或日志。Windows Terminal 会再次解释分号，含分号或双引号的参数直接使用控制台 SSH，从而保留字面值，不依赖未经验证的二次转义。主机 / 用户名中的无效空白、控制字符及无效端口会被拒绝。

仅持有创建时的 Process 句柄并立即释放，不保留或终止用户终端进程。当前不支持配置任意终端程序、跳板机、嵌入式终端或传递已保存密码。

## 验证

Infrastructure 测试使用独立 argv 接收程序，验证空格、Unicode、引号、分号、反斜杠和选项注入形态不会执行其他命令，并实际运行可用的 Windows Terminal。该程序只写入参数并退出。

SSH 测试使用隔离 OpenSSH 8.9p1，Windows OpenSSH 9.5 客户端真实认证到夹具；分别验证关闭终端后转发仍传输、关闭应用 SSH 连接后终端仍可交互，以及应用已信任主机不会自动授权外部 OpenSSH。

本机 ssh-keyscan 9.5 对夹具报告不支持其 sntrup KEX。因此测试的临时 known_hosts 使用另一条已按夹具指纹验证的 SSH 连接获得的公钥；产品启动路径不依赖 ssh-keyscan。测试只使用一次性私钥和隔离 known_hosts。
