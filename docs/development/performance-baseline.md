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
| App 后台稳定 Working Set | 1.11 MiB | 20 秒后由 Windows 回收工作集；受操作系统调度影响较大，不单独作为长期结论 |
| App 稳定 Private Memory | 109.63 MiB | 20 秒后测量 |
| DOCX fixture parse time | 161.204 ms | xUnit 进程首次测试运行，包含小型非敏感 fixture |
| DOCX parse managed allocations | 524,992 B | `GC.GetTotalAllocatedBytes` 差值，不等同进程峰值内存 |
| Snapshot JSON payload | 2,352 B | 两段、两个 Run、简单表格、插入/删除修订、批注 |
| 普通 DOCX 双栏比对内存 | Not measured yet | 完整 Comparison/UI 尚未实现 |
| 大型 DOCX 峰值 | Not measured yet | 尚无大型非敏感 fixture |

## 初步分析

未经处理的 self-contained publish 为 221.97 MiB，其中约 100.19 MiB 是 Skia/HarfBuzz 原生调试符号。Release publish 在复制完成后排除 `.pdb`，运行载荷降至 121.78 MiB；这不是 trimming 或 NativeAOT。调试符号如未来发布时需要，应作为独立符号产物保存，不应进入用户安装目录。

App 启动初期 Working Set 超过目标，但 Private Memory 在本次稳定测量中低于目标。Working Set 会受到 Windows 页面回收、前后台状态和首次 JIT/Migration 影响。Document Engine v0 阶段应增加重复冷/热启动采样，并分别测量未打开文档、打开普通文档和大型文档场景。
