# Dashboard 快速转发区布局调整

用户要求将模式选择合并进 Server 所在行，Endpoints 改为 Advanced 开关，并直接显示 Open terminal；继续精简测试。

## 改动

- `DashboardPage.xaml` 将主操作区排成 Server、Pin、Local / Remote ComboBox、Port、Forward。模式下拉框保留原 `ModeIndex` 绑定，显示简短 Local / Remote 选项。
- Advanced 改用 ToggleSwitch 的 `IsOn` 绑定，继续使用原草稿和字段显示逻辑。Open terminal 从 More 菜单移到选项行，保留原命令处理器和服务器可用性检查。
- `OnQuickFormSizeChanged` 根据快速转发卡片的实际宽度安排布局：宽屏一行，窄屏 Server / Pin 一行、模式 / Port / Forward 一行；Advanced 与终端按钮一起换到下方。
- Dashboard 采用与 Settings 相同的视口 Grid，将 1320 DIP 最大内容宽度放在内部容器，避免宽屏下的错误居中。
- 更新既有原生验证脚本的模式选择与终端定位方式；同步设计和快速转发说明。未更改 SSH 转发、认证、端口校验或终端启动服务。

## 验证范围

只做原生 Visual Studio Release x64 构建及快速转发区的轻量界面检查，不运行完整功能、SSH 或压力回归。证据保存在 `artifacts/dashboard-quick-layout`。

- 构建通过，0 警告、0 错误；`build.log`。
- Local / Remote ComboBox 正常切换，自动找端口选项随模式显示 / 隐藏。
- Advanced 首次打开复制主端口，关闭后再次打开保留编辑后的两个端口；高级模式禁用单端口输入。
- 输入 70000 并点击 Forward 显示端口校验错误，没有启动连接。
- Open terminal 在主卡片直接可见，对已选服务器处于可用状态；没有启动外部终端或 SSH。
- 1200 / 500 DIP 下查看主操作顺序和窄屏换行，所有操作完整显示；`dashboard-wide.png`、`dashboard-narrow.png`、`checks.json`。
- 最终隔离样例窗口保持在 Dashboard，位置和进程见 `review.json`。未重新打发布包、未提交代码；先前未提交工作保留。
