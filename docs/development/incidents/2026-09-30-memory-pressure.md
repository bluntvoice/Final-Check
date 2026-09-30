# 2026-09-30 — Windows 验收期间资源耗尽记录

## 范围与证据边界

Stage A 安装版验收期间发生整机故障。2026-09-30 15:45–15:53（Asia/Shanghai）执行只读排查：Windows 事件日志、性能计数器、进程占用、.NET workload 与 Velopack 日志；没有构建、安装、结束应用、改变页面文件或服务配置。仓库业务代码未修改。

这是开发主机环境记录，不是 Final Check 产品缺陷已定性。用户先前提供的 minidump 分析没有在此次只读排查中重新解析；下表以实际回读事件日志为依据，不将异常码直接等同于具体分配失败。

## 已核实时间线

| 本地时间 | 事实 |
|---|---|
| 14:13:07 | .NET workload 日志记录执行 Final Check 的合成 FixtureTool `dotnet run`，SDK 10.0.401；包含 advertising manifest 提取和 admin-install 操作。 |
| 14:14:18 | MsiInstaller 1040：Microsoft.NET.Workloads.10.0.400 MSI 事务开始，客户端 PID 25680。 |
| 14:14:28 | Resource-Exhaustion-Detector 2004：提交量 51,510,079,488 / 51,736,272,896 bytes（99.563%），547 进程；分页池约 3.992 GiB、非分页池约 1.293 GiB。 |
| 14:14:31 | .NET MSI 成功结束；MSI 日志返回 0。 |
| 14:14:50 | Application Error 1000：DoubaoIME ImeService，异常 0xc0000374、ntdll.dll。 |
| 14:14:55 | Application Error 1000：DingTalk，异常 0xc0000409、Qt5Core.dll +0x20668。 |
| 14:14:59 | Application Error 1000：codex.exe，异常 0xc0000409；WER BEX64 子码 7。 |
| 14:21–14:28 | 后续 .NET 日志记录本轮 Release test / build；不把这些后续操作当作 14:14 MSI 的精确触发命令。 |
| 14:44:03 | Velopack 日志记录 Final Check 0.1.0-dev.12.1 Setup 开始。 |
| 14:46:15 | Final Check 0.1.0-dev.12.1 正常主进程启动。 |

14:14 MSI 日志确认调用者为 `dotnet.exe`、工作目录为仓库 `src/FinalCheck.App`、`ACTION=ADMIN`、`TARGETDIR` 为临时目录，属于 administrative extraction；不能仅凭通用 MsiInstaller “安装成功”事件断言安装了新的整套 SDK。对应 PID 25680 主 workload 日志为空，精确触发命令无法完整还原。

普通 `dotnet build/run/test` 可后台下载 workload advertising manifests，见 [Microsoft 文档](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-workload-install#advertising-manifests)。现有日志与此机制吻合；同期 .NET 活动与测试有关，但缺少当时完整进程峰值/分配栈，不能认定 MSI 是耗尽的根因。新版 Final Check Setup 晚于故障约半小时，不是该次 14:14 故障的触发安装。

## 排查时资源快照

这些值是当时快照，不是后续机器状态，也不是产品性能基准。

| 项目 | 实测 |
|---|---|
| 15:53 系统提交量 | 44.91 / 48.18 GiB，93.2%，剩余约 3.28 GiB |
| Final Check | 单实例 PID 55272，约 208 MiB private bytes |
| Codex Desktop / backend / Node helpers | 合计约 3.18 GiB private bytes；并非只统计名为 codex 的后台进程 |
| dotnet / MSBuild / VBCSCompiler / testhost / msiexec | 当时无残留进程 |
| HipsDaemon PID 3312 | 仍运行，约 3.93 GiB private bytes；与用户此前报告“已不运行”不同 |
| FMService64 PID 5316 | 约 550 万进程句柄；注册服务 FMAPOService，Fortemedia APO Control Service |
| SmartAppearanceSVC PID 5324 | 约 67 万进程句柄；Lenovo Smart Appearance Components |
| 系统分页池 / 非分页池 | 约 3.9 / 1.26 GiB |

大量进程句柄不等于 GDI/USER 窗口对象，GDI/USER 正常不能排除此项异常。尚未确定句柄增长的泄漏原因、内核池与具体服务的归属，未干预服务。单次 Final Check 占用不支持其为主要消耗者，但也不是无泄漏证明。

## 本地证据与保留方式

- 原始日志、事件 XML、性能/进程快照及复制文件 SHA-256 清单保存在 Git 忽略的 `artifacts/diagnostics/2026-09-30-memory-pressure/`，按采集时间建子目录；原始日志不删除、不覆盖。
- 仓库仅保存上述脱敏摘要；不提交 CrashDumps、完整事件 XML、账户/主机身份、合同、数据库或机器日志。原始 `CrashDumps` 仍留在系统原位置，本次没有复制或上传转储。
- 原始证据入口：Windows System 2004 / Application 1000、1001、MsiInstaller、RestartManager；`%LocalAppData%/Temp/Microsoft.NET.Workload_*20260930*.log`；`%LocalAppData%/Velopack/velopack*.log`。
- 原始机器日志可能含账户/路径信息，后续需共享时先审查脱敏，不能把整个 artifacts 目录加入 Git。

## 后续验收策略

用户在了解资源压力后明确要求继续测试，暂不进行系统修复。复用已安装的 `0.1.0-dev.12.1` / SHA `c8c21c89660babb100d5cfad15ec2bdda31132f1`，优先串行轻量 CLI 检查与必要界面试用；不重复已通过的全量 build/test，不启动多个桌面实例或并行重负载。发生新故障时暂停并按新时间戳采集，不用本次快照猜测新原因。

后续若需要 .NET SDK 命令，可考虑仅对当前验证进程使用 `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true` 避免后台 manifest 下载；这是 [官方 CLI 选项](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_cli_workload_update_notify_disable)，不是禁用安全软件或全局修改系统。本次记录不代表已经设置该选项、修复内存问题或完成 Stage A 最终验收。
