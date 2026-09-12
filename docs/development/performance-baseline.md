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

## Windows packaging Spike（2026-09-12）

配置：`.NET 10.0.401 / Runtime 10.0.12 / Avalonia 12.1.2 / Velopack 1.2.0`，Windows x64 Release self-contained，未启用 trimming / NativeAOT；版本 `0.1.0-dev.1`。

| 指标 | Bytes | MiB | 状态 |
|---|---:|---:|---|
| Publish directory | 128,215,648 | 122.28 | 排除 PDB |
| Setup.exe | 61,841,595 | 58.98 | 低于 70 MB 目标和 100 MB 告警 |
| Portable ZIP | 57,167,158 | 54.52 | 已校验 sq.version / About assembly / hash |
| 安装后大小 | — | Not measured yet | Phase 4 实际安装测量；不以 publish 估算替代 |

主要载荷仍是 self-contained .NET Runtime 与 Avalonia/Skia 原生组件；Velopack 压缩后 Setup 低于预算。本机 Spike 产物在被 Git 忽略的 `artifacts/spike-verified-20260912/`，不属于正式 Release。

## Actions Test package / 安装实测（2026-09-12）

来源：[Run 34683055427](https://github.com/bluntvoice/Final-Check/actions/runs/34683055427)，版本 `0.1.0-dev.2.1`，Artifact 已下载并验证 Actions ZIP digest 与逐文件 SHA256。

| 指标 | Bytes | MiB | 说明 |
|---|---:|---:|---|
| Publish | 128,217,746 | 122.28 | Actions win-x64 self-contained |
| Setup | 61,842,439 | 58.98 | 61.84 MB；满足 70 MB 目标 |
| Portable ZIP | 57,168,001 | 54.52 | 完整 portable layout |
| 实际安装目录总大小 | 189,785,631 | 180.99 | 189.79 MB；超过 150 MB 目标，低于 200 MB 告警 |
| `current` runtime payload | 128,146,968 | 122.21 | 不包含用户数据库 |
| `packages` cached full package | 57,204,743 | 54.55 | Velopack 为更新保留的完整包缓存 |
| Update.exe + 启动 stub | 4,433,920 | 4.23 | 安装根目录文件 |

安装总大小不能以 publish 大小替代。超过安装目标的主要原因是 runtime payload 加上缓存 full package；当前不删除 Velopack 缓存来伪造低体积，也不未经验证启用 trimming/NativeAOT。后续可单独评估 runtime/native 载荷和更新缓存策略。

实际 silent 安装 exit 0；普通启动创建响应正常的 `Final Check` 窗口，安装内 About assembly version `0.1.0-dev.2.1`；正常关闭后 silent 卸载 exit 0，卸载器延迟自清理后安装目录/快捷方式/注册项消失。安装 hooks 未改变已有 DB/WAL/SHM 的 hash；普通启动前后数据库表/行 digest 一致；卸载未修改用户数据文件。当前本机 DocumentSnapshots / ComparisonResults 均为空，因此该证据不是含真实合同的业务恢复验收。

### 最终版本复验：0.1.0-dev.3.1

来源：[Run 34683856976](https://github.com/bluntvoice/Final-Check/actions/runs/34683856976)，相同技术与配置，包含最终版本/发布脚本和 81 个测试。

| 指标 | Bytes | MiB |
|---|---:|---:|
| Publish | 128,217,746 | 122.28 |
| Setup | 61,842,430 | 58.98 |
| Portable | 57,167,992 | 54.52 |
| 实际安装目录 | 189,785,622 | 180.99 |

Setup 安装/启动/正常关闭/卸载重复验证 PASS，Portable 的根目录 `Final Check.exe` 实际启动/正常关闭 PASS。Setup 自身 ProductVersion/FileVersion 与 About assembly/Portable/metadata 都为 `0.1.0-dev.3.1`。安装后体积仍超过 150 MB 目标，但低于 200 MB 告警；上述缓存分析不变。
