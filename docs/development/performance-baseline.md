# Final Check v0.1.0 Performance Baseline

## 预算

| 指标 | Target | Warning |
|---|---:|---:|
| Windows 安装包 | ≤ 70 MB | > 100 MB |
| 安装后体积 | ≤ 150 MB | > 200 MB |
| 空闲 Working Set | ≤ 150 MB | — |
| 普通 DOCX 双栏比对 | ≤ 250 MB | — |
| 大型复杂 DOCX 峰值 | preferably ≤ 500 MB | — |

## 测试环境

- 日期：2026-09-10
- OS：Windows x64，build 26100
- .NET SDK：10.0.401
- .NET Runtime：10.0.12
- Avalonia：12.1.2
- 配置：Release、win-x64、self-contained、未启用 trimming/NativeAOT

## 当前实测

| 指标 | 结果 | 说明 |
|---|---:|---|
| Framework-dependent Release build | 25.38 MiB | Desktop 输出目录顶层文件 |
| Self-contained publish | 121.78 MiB | 排除非运行必需 `.pdb`；250 个文件 |
| 安装包 | Not measured yet | 本轮不生成正式安装包 |
| App 启动初期 Working Set | 182.27 MiB | 超过 150 MiB 目标，包含首次数据库 Migration 与 UI/JIT 启动开销 |
| App 后台稳定 Working Set | 1.11 MiB | 初始化阶段历史采样；进程/窗口状态记录不足，已由下方同 PID 复测取代，不作为当前结论 |
| App 稳定 Private Memory | 109.63 MiB | 20 秒后测量 |
| DOCX fixture parse time | 161.204 ms | xUnit 进程首次测试运行，包含小型非敏感 fixture |
| DOCX parse managed allocations | 524,992 B | `GC.GetTotalAllocatedBytes` 差值，不等同进程峰值内存 |
| Snapshot JSON payload | 2,352 B | 两段、两个 Run、简单表格、插入/删除修订、批注 |
| 普通 DOCX 双栏比对内存 | Not measured yet | 完整 Comparison/UI 尚未实现 |
| 大型 DOCX 峰值 | Not measured yet | 尚无大型非敏感 fixture |

## 初步分析

未经处理的 self-contained publish 为 221.97 MiB，其中约 100.19 MiB 是 Skia/HarfBuzz 原生调试符号。Release publish 在复制完成后排除 `.pdb`，运行载荷降至 121.78 MiB；这不是 trimming 或 NativeAOT。调试符号如未来发布时需要，应作为独立符号产物保存，不应进入用户安装目录。

App 启动初期 Working Set 超过目标，但 Private Memory 在本次稳定测量中低于目标。Working Set 会受到 Windows 页面回收、前后台状态和首次 JIT/Migration 影响。Document Engine v0 阶段应增加重复冷/热启动采样，并分别测量未打开文档、打开普通文档和大型文档场景。

## Document Engine v0 复测（2026-09-10）

### 桌面 App 空闲采样

使用 framework-dependent Release build 启动 `FinalCheck.Desktop.exe`，对同一进程连续采样。没有调用 `EmptyWorkingSet`、强制 GC 或其他工作集清理。每次采样时窗口句柄均为 `1873027432`、进程 `Responding=True` 且未退出。

| 时间 | PID | App 状态 | Working Set | Private Memory |
|---:|---:|---|---:|---:|
| 5s | 52780 | 主窗口存在、响应正常 | 170.99 MiB | 105.24 MiB |
| 10s | 52780 | 主窗口存在、响应正常 | 171.09 MiB | 105.32 MiB |
| 20s | 52780 | 主窗口存在、响应正常 | 15.04 MiB | 105.39 MiB |

20 秒 Working Set 的下降发生在相同、仍响应的进程中，但仍可能是 Windows 页面回收结果，不代表托管堆或应用真实私有占用降至 15.04 MiB。Private Memory 在三次采样中更稳定，因此当前更适合观察空闲内存趋势。外部采样无法可靠读取应用内部 GC 状态；本次没有人为触发 GC。

### Document Engine 综合 fixture

配置：Release；fixture 为 250 个正文 Paragraph、12 个 Table、48 个 Cell；测试进程 PID 26420。

| 指标 | 结果 |
|---|---:|
| Parse time | 110.502 ms |
| Managed allocations | 4,127,784 B（3.94 MiB） |
| 测试进程 Working Set（前 → 后） | 72.65 MiB → 87.45 MiB |
| 测试进程 Private Memory（前 → 后） | 25.63 MiB → 37.71 MiB |
| Snapshot JSON | 1,016,466 B（0.97 MiB） |
| GZip | 48,731 B（0.05 MiB） |
| Brotli | 27,379 B（0.03 MiB） |
| Serialize | 132.803 ms |
| Deserialize | 71.751 ms |
| GZip | 1.864 ms |
| Brotli | 1.041 ms |

该结果是确定性合成文档的单次开发机基线，不代表大型真实合同上限。Brotli 在本次样本更小，但 v0 继续以原始 UTF-8 JSON 保存 SQLite payload，避免在缺少真实数据分布与存储迁移设计时过早固定压缩格式。后续基线只追加新日期记录，不覆盖本次结果。
