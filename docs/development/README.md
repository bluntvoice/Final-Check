# Final Check Development

## 项目状态

Final Check v0.1.0 已完成基础工程初始化，Document Engine v0 正在按 [`docs/tasks/document-engine-v0.md`](../tasks/document-engine-v0.md) 验收。完整 Comparison Engine、正式业务 UI、格式恢复、安装包和 Release 尚未实现。

## 开发前阅读

1. [`AGENTS.md`](../../AGENTS.md)
2. [`docs/PRD/PRD-v0.1.0.md`](../PRD/PRD-v0.1.0.md)
3. 与任务相关的 [architecture 文档](../architecture/README.md)
4. [性能基线](performance-baseline.md)

## Windows 开发环境

- Windows 10 22H2 或 Windows 11 x64
- .NET 10 SDK（当前基线 10.0.401）
- Git
- 可选 IDE：Visual Studio、JetBrains Rider 或 VS Code
- 不需要安装 Microsoft Word、Office Interop 或单独的 SQLite 服务

仓库 `global.json` 固定稳定 SDK feature band，并禁止 prerelease SDK。Avalonia 与项目 NuGet 包在各 `.csproj` 中使用明确稳定版本。

## 常用命令

在仓库根目录执行：

```powershell
dotnet tool restore
dotnet restore FinalCheck.sln
dotnet build FinalCheck.sln
dotnet test FinalCheck.sln
dotnet run --project src/FinalCheck.Desktop/FinalCheck.Desktop.csproj
```

Release 验证：

```powershell
dotnet build FinalCheck.sln --configuration Release
dotnet restore src/FinalCheck.Desktop/FinalCheck.Desktop.csproj --runtime win-x64
dotnet publish src/FinalCheck.Desktop/FinalCheck.Desktop.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --no-restore
```

当前 Release publish 会排除用户运行不需要的 `.pdb` 调试符号，但不启用 trimming 或 NativeAOT。

## Document Engine 开发与测试

Document Engine 架构与已知限制见 [`document-engine.md`](../architecture/document-engine.md)。解析测试使用 `DocumentFixtureFactory` 在内存中生成小型 DOCX，不使用真实合同。夹具目录说明见 [`tests/fixtures/README.md`](../../tests/fixtures/README.md)。

常用聚焦验证：

```powershell
dotnet test tests/FinalCheck.Documents.Tests/FinalCheck.Documents.Tests.csproj
dotnet test tests/FinalCheck.Data.Tests/FinalCheck.Data.Tests.csproj `
  --filter DocumentSnapshotStorePersistsSerializedSnapshotRoundTrip
```

新增 Open XML 能力时，应增加一个只隔离该结构的程序化 fixture 和聚焦断言。只有 SDK 无法可靠构造的极小结构才保存二进制 DOCX，并在 fixture README 说明原因。不得提交真实合同。

性能测试使用固定的 `PerformanceComposite` fixture。该测试会输出解析、序列化、反序列化、managed allocation、JSON/GZip/Brotli 大小及测试进程内存；压缩目前只比较，不改变 SQLite payload：

```powershell
dotnet test tests/FinalCheck.Documents.Tests/FinalCheck.Documents.Tests.csproj `
  --configuration Release `
  --filter PerformanceCompositeRecordsJsonAndCompressionBaseline `
  --logger "console;verbosity=detailed"
```

## Solution 结构

```text
src/
  FinalCheck.App/             Avalonia Views、ViewModels、Styles、Navigation
  FinalCheck.Desktop/         桌面入口与 DI Composition Root
  FinalCheck.Core/            纯领域模型和稳定接口
  FinalCheck.Documents/       Open XML 与 DocumentSnapshot
  FinalCheck.Comparison/      Snapshot 比较引擎
  FinalCheck.Data/            SQLite、EF Core、Migration
  FinalCheck.Infrastructure/  文件、路径、哈希、预览、更新等外围实现
tests/
  FinalCheck.Core.Tests/
  FinalCheck.Documents.Tests/
  FinalCheck.Comparison.Tests/
  FinalCheck.Data.Tests/
```

依赖方向和硬性边界见 [`docs/architecture/README.md`](../architecture/README.md)。

## 数据库与 Migration

运行时数据库路径统一由 `IAppDataPathProvider` 提供。Windows 当前路径为：

```text
%LOCALAPPDATA%\FinalCheck\finalcheck.db
```

EF 工具作为仓库本地工具管理：

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add <MigrationName> `
  --project src/FinalCheck.Data/FinalCheck.Data.csproj `
  --output-dir Migrations
```

数据库时间统一使用 UTC；UI 层负责转换为本地时间。

## 平台政策

Windows 是官方开发、测试和未来 Release 平台。macOS 目前仅检查 Core、Documents、Comparison、Data 及其测试的源码构建兼容性，不提供官方安装包或完整质量保证。核心项目禁止引入 Windows-only API；平台实现必须放在 Infrastructure/Desktop。

## 代码质量

- Nullable、Implicit Usings、内置 .NET Analyzer 已启用。
- 不为初始化阶段一次引入大量第三方 Analyzer。
- Open XML Package 必须短生命周期并及时 Dispose。
- 日志不得默认写入完整合同正文、批注或大段修改内容。
- 新增大依赖前必须评估包体积、内存和跨平台成本。

## 性能预算

- Windows 安装包：目标 ≤70 MB，>100 MB 告警。
- 安装后体积：目标 ≤150 MB，>200 MB 告警。
- Idle Working Set：目标 ≤150 MB。
- 普通 DOCX 双栏比对：目标 ≤250 MB。
- 大型复杂 DOCX 峰值：尽量 ≤500 MB。

当前实测见 [`performance-baseline.md`](performance-baseline.md)。
