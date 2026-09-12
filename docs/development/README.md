# Final Check Development

## 项目状态

Final Check v0.1.0 的基础工程、Document Engine v0、Comparison Engine v0 与 Format Restore Engine v0 已按各任务文件实现，验收状态以 task 为准。Windows Test Build / 正式发布基础设施见 [Release process](release-process.md)；尚未发布真实 Stable Release，也未实现正式业务 UI 或 updater UI。

可选安装目录、独立可迁移 DataRoot / bootstrap 已完成产品与架构落档，**尚未实现** Installer Wizard / Storage Settings / 数据迁移，不改变现有 Velopack workflow 或运行时数据路径。

## 开发前阅读

1. [`AGENTS.md`](../../AGENTS.md)
2. [`docs/PRD/PRD-v0.1.0.md`](../PRD/PRD-v0.1.0.md)
3. 与任务相关的 [architecture 文档](../architecture/README.md)
4. [性能基线](performance-baseline.md)

## Windows 开发环境

- Windows 10 22H2 或 Windows 11 x64
- .NET 10 SDK（当前基线 10.0.401）
- PowerShell 7+（版本和 Windows 打包/发布脚本）
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

## 版本与 Windows 包

```powershell
./scripts/version.ps1 print
./scripts/test-version.ps1
./scripts/test-release-infrastructure.ps1
./scripts/build-windows-package.ps1 -PackageKind Test -Version 0.1.0-dev.123 `
  -OutputDirectory artifacts/local-test-123
./scripts/verify-windows-package.ps1 -Directory artifacts/local-test-123 `
  -Version 0.1.0-dev.123 -PackageKind Test
```

OutputDirectory 必须为空；本地 Test 包只覆盖构建参数，不改源码版本。`dotnet tool restore` 还原固定的 EF / Velopack CLI。正式 Release 不能用此本地命令替代确认与安全门禁，完整操作参见 [Release process](release-process.md)。

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

## Format Restore 开发验证

先读 [task](../tasks/format-restore-engine-v0.md)、[architecture](../architecture/format-restore-engine.md) 与 ADR-0005。测试使用生成式非敏感 DOCX 和 GUID 隔离 SQLite/AppData；不得用用户合同或正常数据库试验迁移/Undo。

```powershell
dotnet test -c Release --filter FullyQualifiedName~FormatRestore
dotnet test tests/FinalCheck.Data.Tests/FinalCheck.Data.Tests.csproj -c Release `
  --filter SmallAndMediumRecordPlanExecutionReparseAllocationsAndWorkingSize `
  --logger "console;verbosity=detailed"
dotnet run --project src/FinalCheck.Desktop/FinalCheck.Desktop.csproj -c Debug -- `
  --developer-data-directory C:\Temp\FinalCheck-Isolated-Developer-Check
```

Debug-only `--developer-data-directory` 必须显式绝对路径，拒绝正常 FinalCheck 数据目录；Release 包无此配置。它只用于启动/DI/迁移冒烟，不是正式 UI。底层服务调用方提供稳定 ContractVersion Guid、Snapshot/Comparison 与 Plan；外部编辑必须明确 preserve/regenerate，不自动猜测。当前数据库 schema 3 新增 restore history，旧 Snapshot/Comparison 保留。

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
  FinalCheck.App.Tests/        About / assembly 版本展示测试（无 GUI 初始化）
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

## 安装与数据路径后续开发

先读 [Storage and paths](../architecture/storage-and-paths.md)、[Installer Architecture / Spike](../architecture/installer-architecture.md)、[ADR-0006](../architecture/ADR-0006-install-location-and-data-root.md) 与 [Release process](release-process.md) 的待实施门禁。

- 安装目录与数据目录独立；业务数据绝不写入可替换/卸载的安装目录。首装可见目录/Browse、三类包同一机制、维护/更新/卸载沿用实际位置是正式要求，不是当前原生 Setup 的既有能力。
- 新用户正式默认 DataRoot `%LocalAppData%\FinalCheck\Data`，locator `%LocalAppData%\FinalCheck\bootstrap.json`；当前旧库 `%LocalAppData%\FinalCheck\finalcheck.db` 保持不变。没有 bootstrap 不能直接判断为新用户并创建空库。
- `IDataRootProvider`、bootstrap store、path policy、maintenance coordinator、session factory、migration/usage service 只完成职责设计，生产代码仍使用 `IAppDataPathProvider`。不单独增加可变路径字段而遗漏旧 scope/连接池、managed absolute path/history 和跨进程写入。
- Storage Settings 在隔离固定磁盘/生成式有数据 SQLite 和 DOCX 中验证复制、WAL、SHA/数量/大小、重新打开/重解析、locator 提交与 crash 恢复；失败保护旧 root，成功默认保留旧数据，立即刷新 UI/统计。不能用用户库试迁移或直接剪切。
- Snapshot / Comparison / restore payload 当前包含在 SQLite，逻辑 payload 统计不能与 Database 物理大小重复累加；统计跟随 root generation。原始 DOCX 路径与 PRD 导出位置不变。
- Windows fixed-disk 路径、权限/空间、同步目录/网络/可移动/链接阻断属于 Infrastructure/Desktop；Core/Documents/Comparison/Data 不引入 Registry、Shell 等 API。安装器 Spike 未通过前不改发布流水线或升级 Velopack 依赖。

## 平台政策

Windows 是官方开发、测试和 Release 平台。macOS 目前仅检查 Core、Documents、Comparison、Data 及其测试的源码构建兼容性，不提供官方安装包或完整质量保证。核心项目禁止引入 Windows-only API；平台实现必须放在 Infrastructure/Desktop。

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
