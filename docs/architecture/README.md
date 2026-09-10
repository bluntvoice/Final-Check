# Final Check Architecture

## 当前技术基线

Final Check v0.1.0 使用 .NET 10、C#、Avalonia UI、MVVM、Open XML SDK、SQLite、EF Core 10 和 xUnit。官方开发与主要测试以 Windows 为主，核心项目保持跨平台兼容。

技术选型依据见 [`ADR-0001-technology-stack.md`](ADR-0001-technology-stack.md)。

## Solution 分层

| 项目 | 职责 | 允许的主要依赖 |
|---|---|---|
| `FinalCheck.App` | Avalonia Application、Views、ViewModels、样式与导航 | Core、Avalonia、MVVM Toolkit |
| `FinalCheck.Desktop` | 桌面入口、DI Composition Root、生命周期与平台初始化 | 所有运行时项目 |
| `FinalCheck.Core` | 领域模型、Snapshot、跨层接口 | .NET BCL |
| `FinalCheck.Documents` | DOCX/Open XML 解析、Snapshot 序列化、后续格式恢复 | Core、Open XML SDK |
| `FinalCheck.Comparison` | Snapshot 结构匹配、文字/格式 Diff 与归并接口 | Core |
| `FinalCheck.Data` | SQLite、EF Core、Migration、持久化 Entity | Core、EF Core |
| `FinalCheck.Infrastructure` | 文件、哈希、数据路径、预览、更新等外围实现 | Core |

## 硬性边界

- Core、Documents、Comparison、Data 禁止依赖 Word COM、Office Interop、Word.exe、Registry、explorer.exe、Windows Shell、WinUI 或 Windows App SDK。
- Open XML Package 只在解析期间存活；生成 Snapshot 后立即释放。
- Comparison Engine 面向 `DocumentSnapshot`，不直接以两个文件路径承载完整业务。
- Domain Snapshot 通过序列化 Payload 持久化，不与 EF Entity 完全耦合。
- 平台能力必须通过接口进入 Infrastructure/Desktop。
- 日志不得默认记录完整合同正文、批注或大段修改内容。

## 已建立的 v0 基础

- Snapshot schema version 与 Paragraph/Run/Table/Revision/Comment 模型。
- `IDocumentParser`、`IDocumentSnapshotSerializer`、`IDocumentFormatService`、`IDocumentPreviewRenderer`。
- `IComparisonEngine`、`IStructureMatcher`、`ITextDiffService`、`IFormatDiffService`、`IChangeGroupingService`。
- `IAppDataPathProvider`、`IFileHashService`、`IUpdateService`。
- SQLite 初始 Migration 与 UTC 时间转换。
- `DocumentNodeMapping` 格式恢复映射预留。

当前 Comparison 仅为可替换的最小位置匹配/整段差异骨架，不代表 PRD 所需完整合同 Diff 已完成。
