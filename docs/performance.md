# 性能、内存与规模验证

## 2026-09-23 Release x64 收尾

当前包为 `TunnelDeck-1.0.0-win-x64.zip`。本轮减少了未使用的控件依赖、隐藏表单控件和重复资源行；**整进程私有内存没有显示出明确、可重复的下降**。数值及逐次导航采样保存在 [本轮性能证据](evidence/2026-09-23-release-performance.json)，没有使用强制 GC 或工作集裁剪作为应用运行策略。

具体改动：移除未使用的直接 CommunityToolkit 控件引用及旧 Microsoft.UI.Xaml 2.8.7；Dashboard Advanced 与 Settings 私钥区域按需创建；Shell 复用服务器 / Profile 展示行及排序列表，在资源或顺序变化时失效缓存。没有改变转发引擎或保存语义。

### 导航与托管留存

250 Server + 250 Profile，无 SSH；预热一次后循环 Servers → Profiles → Settings → Dashboard 12 次。`dotnet-gcdump` 的堆快照会触发完整 GC，以下留存采样不能当作普通空闲状态。

| 指标 | 优化前 | 最终发布包 |
| --- | --- | --- |
| 启动 Private Bytes | 149.77 MiB | 150.14 MiB |
| 预热后、GC 后 Private Bytes | 184.40 MiB | 199.32 MiB |
| 最后一轮、GC 后 Private Bytes | 248.17 MiB | 254.62 MiB |
| 最后一轮、GC 后 Working Set | 301.95 MiB | 299.03 MiB |
| 托管堆留存 | 4,972,831 bytes | 4,972,883 bytes |
| ResourceRow 实例 | 750 | 540 |
| NumberBox 实例 | 8 | 4 |
| TextBox 实例 | 9 | 1 |

另一轮同组优化的普通 Release 构建测得最终 Private Bytes 为 242.57 MiB，说明进程总量存在运行波动，不能只选较低的一次宣称节省内存。最终堆快照中 Dashboard 与 Settings 各保留两个实例，未随 12 次循环持续累加；这不替代长时间运行的泄漏分析。托管堆约 4.74 MiB，也不能代替包含原生控件、运行时及其他分配的进程总量。

### 最终发布包的真实转发

1 个隔离服务器、1 个包含 20 条规则的 Profile，运行 10 条 Local + 10 条 Remote；没有使用用户服务器。

| 指标 | 空闲 | 20 条运行 | Stop 后即时采样 |
| --- | --- | --- | --- |
| Private Bytes | 143.05 MiB | 202.01 MiB | 208.79 MiB |
| Working Set | 199.50 MiB | 271.86 MiB | 278.98 MiB |
| 句柄 | 1286 | 1868 | 1681 |
| 线程 | 80 | 116 | 106 |

60.08 秒空闲采样只消耗 0.015625 CPU 秒，约 0.026% 单核 / 0.0011% 整机（24 个逻辑处理器）。四次启动至响应窗口为 0.807–1.055 秒；没有清空 OS 文件缓存，不称为磁盘冷启动。20 条转发均验证了实际 payload，Stop 后所有本地和远端监听均释放；正常关闭约 292 ms。Stop 后即时内存没有强制 GC，不能由此单独认定泄漏。

本机使用 .NET 10.0.9、Windows App Runtime 2.5.1；本次进程中也检测到 RTSSHooks64，未更改用户叠加层设置。这里记录本机环境的实测值，不承诺其他机器具有相同占用。

### 重现本轮测量

```powershell
./scripts/debug/Measure-Application.ps1 -NoFixtureBuild -ExecutablePath ./artifacts/release/TunnelDeck-1.0.0-win-x64/TunnelDeck.exe

. ./scripts/Enter-DevelopmentShell.ps1
dotnet tool install dotnet-gcdump --version 10.0.745401 --tool-path ./.test-output/diagnostics
./scripts/debug/Measure-Memory.ps1 -ExecutablePath ./artifacts/release/TunnelDeck-1.0.0-win-x64/TunnelDeck.exe -GcDumpPath ./.test-output/diagnostics/dotnet-gcdump.exe -OutputDirectory ./artifacts/memory
```

本轮验收结束后清理原始转储、截图、临时工具和构建输出；上述持久证据保留逐次采样、堆对象计数及真实转发结果。下面的 M13 数值属于早期 UI 的历史基线。

## M13 历史性能与规模验证

2026-09-21，同一台 M0 开发机：Windows 11 Pro 25H2 / 26200.9445、AMD Ryzen 9 5900X（12 核 / 24 逻辑处理器）、64 GiB 内存。Release x64，.NET Runtime 10.0.9、Windows App Runtime 2.5.1 使用共享安装，RivaTuner 已由用户关闭。环境原始记录为 `artifacts/m13-review/environment.json`。

## 启动与空闲

| 指标 | M13 实测 | 口径 |
| --- | --- | --- |
| 首次进程启动至响应窗口 | 812 ms | 没有清空 OS 文件缓存，不冒充真正磁盘冷启动 |
| 后三次进程启动至响应窗口 | 623–767 ms | 新进程、相同数据目录 |
| 启动并可编辑，含 UIA 检查 | 1019–1203 ms | 包含测试驱动进程与 IPC 开销 |
| 空闲采样 | 60.91 s | 无转发的实际发布程序 |
| CPU 累计 | 0.1094 CPU s | 只统计 TunnelDeck 进程 |
| 空闲平均 CPU | 0.1796% 单核 / 0.00748% 整机 | 整机值除以 24 个逻辑处理器，低于 1% 目标 |
| 空闲 Private Bytes / Working Set | 118.5 / 172.2 MiB | 1 台服务器、1 个包含 20 条规则的 Profile |
| 空闲句柄 / 线程 | 1243 / 82 | 单次进程采样 |

M0 空白模板同机记录约 91.3 MiB Private Bytes、123.5 MiB Working Set、1066 个句柄 / 77 个线程。M13 数据集、功能和窗口内容不同，不能把差值全部归为泄漏。M4 没有留下已确认的固定数值内存预算，本轮交付实测值，不虚构预算验收。

## 20 条真实转发

一个隔离 SSH 服务器上运行 10 条 Local + 10 条 Remote，逐条验证实际 payload。Profile 只弹一次临时密码输入；没有使用用户服务器或保存测试密码。

| 指标 | 启动前 | 20 条已运行 | Stop 后即时采样 |
| --- | --- | --- | --- |
| Private Bytes | 118.5 MiB | 190.7 MiB | 198.0 MiB |
| Working Set | 172.2 MiB | 255.6 MiB | 265.1 MiB |
| 句柄 | 1243 | 1893 | 1751 |
| 线程 | 82 | 120 | 123 |

运行增量约 72.2 MiB Private Bytes / 83.4 MiB Working Set。全部 20 条 payload 通过，Stop 后本地端口可重新绑定、远端监听均释放；20 次启动 / 20 次停止历史已保存，正常关闭约 275 ms。

Stop 后数字包括 CLR 保留的堆、线程池、导航与历史 UI，不是强制 GC 后的泄漏检测。独立 100 次真实启停测试使用受控 GC 和释放断言：200 个传输全部释放，预热后句柄 -14、线程 -3、Working Set 约 -5.4 MiB。该测量来自测试宿主，不能冒充 GUI 进程的 100 次循环结果；也不替代数日负载和 TCP half-close 验证。

## 1000 Server + 1000 Profile

原先的外部 ScrollViewer 给 ListView 无限高度，UIA 全树遍历期间出现暂时不响应，并采样到约 563 MiB Private Bytes。改为两个有界原生 ListView 后，保留 Pinned / Other 独立滚动：Pinned 在两组都存在时最多占 40%，只有 Pinned 时可用整个视口。

每页约 53 个实际行按钮，最后一项可滚动到达，Sidebar 每类最多 5 项；搜索可正确打开第 1000 台服务器，浏览不写配置。Server / Profile 页的 Private Bytes 约为 164 / 186 MiB。原基线在 UIA 遍历期间失败，新值是完成遍历后的采样，不将两者标为严格峰值比较。

搜索 Core 5 种查询、每种 10 次预热 + 50 次采样，最重的全匹配查询 P95 约 53 ms。实际 GUI 使用隔离实例的可选计时器测量同步处理，排除 UIA / IPC、网络与动画完成：

| 操作 | 样本数 | P50 | P95 | 最大值 |
| --- | --- | --- | --- | --- |
| 导航 | 25 | 7.50 ms | 31.98 ms | 116.08 ms |
| 搜索 | 47 | 19.05 ms | 38.41 ms | 97.37 ms |

两项 P95 均低于 100 ms。保留初始化最大值，不宣称每次操作都低于目标。计时只在显式隔离数据目录且提供 `TUNNELDECK_UI_TIMINGS` 时启用，仅保存操作名和耗时，不包含资源名称、端点、凭据或输入文本。

## 重现与证据

```powershell
./scripts/debug/Verify-ResourceScale.ps1
./scripts/debug/Measure-UiPerformance.ps1
./scripts/debug/Measure-Application.ps1 -NoFixtureBuild
```

原始数据位于 `artifacts/m13-review/resource-scale-after`、`ui-performance-native-input`、`application-performance` 和 `lifecycle/cycles.json`。性能测量与其他负载测试分开执行；原生脚本顺序运行，保留前台所有权检查。
