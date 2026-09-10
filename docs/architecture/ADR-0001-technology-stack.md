# ADR-0001：Final Check v0.1.0 技术栈与跨平台边界

- 状态：Accepted
- 日期：2026-09-10
- 适用版本：v0.1.0

## 背景

Final Check 的核心难点是 DOCX/Open XML 结构、修订、批注、格式、表格、格式恢复和多版本比较。产品以 Windows 10 22H2、Windows 11 x64 为官方开发与主要测试目标，同时保留社区用户从源码构建 macOS 或 Linux 版本的可能性。

## 决策

- 语言与运行时：C# 14、.NET 10 LTS。
- UI：Avalonia UI 12，采用 MVVM。
- DOCX：DocumentFormat.OpenXml 3.5.1，不依赖 Microsoft Word。
- 数据：SQLite、Entity Framework Core 10；数据库时间统一存 UTC。
- 测试：xUnit。
- 依赖注入：Microsoft.Extensions.DependencyInjection。
- Windows 正式发布优先采用 self-contained；当前不启用 trimming 或 NativeAOT。
- `FinalCheck.Core`、`FinalCheck.Documents`、`FinalCheck.Comparison`、`FinalCheck.Data` 不得依赖 Windows-only API。

## 分层与依赖方向

```text
FinalCheck.App            ─┐
FinalCheck.Documents      ├─> FinalCheck.Core
FinalCheck.Comparison     ┤
FinalCheck.Data           ┤
FinalCheck.Infrastructure ┘

FinalCheck.Desktop -> 上述各项目（Composition Root）
```

Core 只保存领域模型和稳定接口。Documents 把短生命周期的 Open XML DOM 转换成带 `SnapshotSchemaVersion` 的 `DocumentSnapshot`，随后及时释放 Package。Comparison 面向 Snapshot，不直接把两个文件路径当作完整比较输入。Data 负责 SQLite、EF Core 和 Migration，但不把领域 Snapshot 与 EF Entity 完全耦合。平台实现集中在 Infrastructure，Desktop 负责注册和启动。

## 为什么选择 Avalonia，而不是 WinUI 3

WinUI 3 会把 UI 和工程入口绑定到 Windows App SDK，不符合保留 macOS 源码构建能力的约束。Avalonia 提供桌面跨平台能力，同时可继续使用 .NET、C#、MVVM 和本地 UI。

## 为什么不选择 Tauri

Tauri 本身可跨平台，但会引入 Rust 与 Web 前端的双技术栈，并使 .NET/Open XML/EF Core 核心与 UI 之间增加进程或绑定边界。本项目的主要复杂度不在 Web UI，统一使用 .NET 更利于控制架构和维护成本。

## 为什么不选择 Electron

Electron 需要内置完整 Chromium/Node Runtime，会显著增加包体积与空闲内存，不符合轻量桌面工具的性能预算。

## 为什么 Document Engine 不依赖 Word

Microsoft Word COM、Office Interop 和 Word.exe 仅适用于特定 Windows/Office 环境，也不能可靠处理限制编辑或无 Office 的场景。Documents 使用 Open XML SDK 直接读取 DOCX 包，核心解析无需安装 Microsoft Word。

## 为什么 Comparison Engine 面向 Snapshot

文件路径不是稳定的业务输入；原始文件可能移动、改名、删除或被覆盖。结构化 Snapshot 能保留当次解析事实、支持历史查看和 schema 迁移，也能把解析与比较分别测试和演进。

## 为什么平台能力必须抽象

应用数据目录、打开文件、通知、更新和安装等能力在不同操作系统上的实现不同。接口放在稳定层、实现放在 Infrastructure/Desktop，避免 Windows API 渗透到核心项目。

## 影响与后续验证

- 精确页码不是 Open XML 的天然能力，需要独立 Spike。
- 文档预览不得绑定 Word COM 或完整 Chromium Runtime。
- trimming、NativeAOT、正式安装包和 updater 均需单独兼容性验证。
- 引入大型依赖前必须测量 publish 体积和内存影响。
