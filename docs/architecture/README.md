# Final Check Architecture

## 当前技术基线

Final Check v0.1.0 使用 .NET 10、C#、Avalonia UI、MVVM、Open XML SDK、SQLite、EF Core 10 和 xUnit。官方开发与主要测试以 Windows 为主，核心项目保持跨平台兼容。

技术选型依据见 [`ADR-0001-technology-stack.md`](ADR-0001-technology-stack.md)。

Document Engine v0 的解析管线、Snapshot schema 与已知限制见 [`document-engine.md`](document-engine.md)；schema 与 payload 持久化决策见 [`ADR-0002-document-snapshot-schema-and-persistence.md`](ADR-0002-document-snapshot-schema-and-persistence.md)。

Comparison Engine v0 的结果模型、匹配管线和确定性规则见 [`comparison-engine.md`](comparison-engine.md)；Comparison 领域模型归属与多信号段落匹配决策见 [`ADR-0003-comparison-model-and-paragraph-matching.md`](ADR-0003-comparison-model-and-paragraph-matching.md)。

Windows 打包、单一版本源、安装/数据目录分离与发布 channel 决策见 [`ADR-0004-windows-packaging-and-release.md`](ADR-0004-windows-packaging-and-release.md)。Velopack 依赖仅进入 Desktop，不改变核心四层的跨平台边界。

可选安装目录与可迁移 DataRoot 的正式决策见 [`ADR-0006-install-location-and-data-root.md`](ADR-0006-install-location-and-data-root.md)。[`installer-architecture.md`](installer-architecture.md) 记录 Velopack 1.2.0 调查、推荐 Wizard 包装和待执行 Spike；[`storage-and-paths.md`](storage-and-paths.md) 定义 bootstrap、路径政策、兼容、非破坏迁移及接口设计。两者目前是设计，不是已启用功能，不改变现有 packaging/runtime。

Format Restore Engine v0 的保守映射、最小属性写回与安全验证见 [`format-restore-engine.md`](format-restore-engine.md)；固定 Working Copy、journal、Undo 与持久化决策见 [`ADR-0005-format-restore-working-copy-and-undo.md`](ADR-0005-format-restore-working-copy-and-undo.md)。

## Solution 分层

| 项目 | 职责 | 允许的主要依赖 |
|---|---|---|
| `FinalCheck.App` | Avalonia Application、Views、ViewModels、样式与导航 | Core、Avalonia、MVVM Toolkit |
| `FinalCheck.Desktop` | 桌面入口、DI Composition Root、生命周期与平台初始化 | 所有运行时项目 |
| `FinalCheck.Core` | 领域模型、Snapshot、跨层接口 | .NET BCL |
| `FinalCheck.Documents` | DOCX/Open XML 解析、Snapshot 序列化、格式属性恢复/验证/Undo | Core、Open XML SDK |
| `FinalCheck.Comparison` | Snapshot 结构匹配、文字/格式 Diff、归并与格式恢复规划 | Core |
| `FinalCheck.Data` | SQLite、EF Core、Migration、持久化 Entity | Core、EF Core |
| `FinalCheck.Infrastructure` | 文件、哈希、数据路径、预览、更新等外围实现 | Core |

## 硬性边界

- Core、Documents、Comparison、Data 禁止依赖 Word COM、Office Interop、Word.exe、Registry、explorer.exe、Windows Shell、WinUI 或 Windows App SDK。
- Open XML Package 只在解析期间存活；生成 Snapshot 后立即释放。
- Comparison Engine 面向 `DocumentSnapshot`，不直接以两个文件路径承载完整业务。
- Domain Snapshot 通过序列化 Payload 持久化，不与 EF Entity 完全耦合。
- 平台能力必须通过接口进入 Infrastructure/Desktop。
- Application install location and application data location are independent concepts.
- User data must never be stored inside the replaceable application installation directory.
- 日志不得默认记录完整合同正文、批注或大段修改内容。

## 已建立的 v0 基础

- Snapshot schema version 与 Paragraph/Run/Table/Revision/Comment 模型。
- `IDocumentParser`、`IDocumentSnapshotSerializer`、`IDocumentFormatService`、`IDocumentPreviewRenderer`。
- `IComparisonEngine`、`IStructureMatcher`、`ITextDiffService`、`IFormatDiffService`、`IChangeGroupingService`。
- `IAppDataPathProvider`、`IFileHashService`、`IUpdateService`。
- SQLite 初始 Migration 与 UTC 时间转换。
- `DocumentNodeMapping` 格式恢复映射预留。
- Format Restore Plan/Scope、可信 ComparisonNodeMapping、属性级 Renderer、固定 Working Copy/Undo/external-edit service；database schema 3 operation history。

Comparison Engine v0 已完成段落匹配、文字 Diff、结构变化、格式 Diff、修订/批注归并与结果持久化；验收和已知限制以任务文件为准。Windows packaging / Release 基础设施独立于业务引擎，软件内 updater UI 仍未实现。

`IDataRootProvider` / bootstrap / 全局存储迁移屏障及 Storage Settings 仅完成职责设计，尚未进入“已建立的 v0 基础”；当前 `IAppDataPathProvider` 的旧目录不自动变更。
