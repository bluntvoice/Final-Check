# Final Check Development

## 项目状态

Final Check v0.1.0 的基础工程、Document / Comparison / Format Restore Engine、Comparison UI 与 Project / Template / Version Management 已按各任务实施，验收状态以 task 为准。项目管理 schema 9 由增量迁移建立模板、项目、明确角色/轮次的批量版本、当前我方基准、独立比对历史和可恢复归档/回收站。Quick Compare 共用 Engine/result/review UI，已审阅不表示接受。新我方导入默认保留基准，冻结 Snapshot 不随源文件或模板修改重算。永久删除二次确认、引用保护与持久清理日志详见 [架构](../architecture/project-template-version-management.md)。Windows Test Build / 正式发布基础设施见 [Release process](release-process.md)；未创建 Stable Release，updater UI 尚未实现。

可选安装目录已完成架构落档，Installer Wizard 尚未实现。Storage Foundation v0 Phase 1–5 基础实现完成：bootstrap / 旧库识别、路径政策、非破坏迁移 / session rebind、启动 recovery 和 usage；验收证据以 [storage task](../tasks/storage-foundation-v0.md) 为准。Stage B 已接入正式 Storage Settings UI，实际状态见 [Stage B task](../tasks/installer-and-storage-settings-v0.md)；Velopack workflow 未改变。

## 后续开发顺序与验收边界

用户于 2026-10-05 选择先进入 Stage C，保留 Stage B 安装器待验项。[Format Restore UI task](../tasks/format-restore-ui-v0.md) 是当前恢复 UI 工作入口；C1 为正式工作台只读计划/属性预览与选择，后续确认执行、Undo/外部编辑/导出和最终测试包验收分别记录，不能把预览等同于完整恢复 UI 已交付。

[当前 PRD](../PRD/PRD-v0.1.0.md) 已按 2026-09-14 试用反馈校准，第 64 章是后续顺序：Stage A 易用性/自动项目化/版本轮次简化/模板推荐及绑定默认/一体工作台/Undo/逻辑滚动/Ignore Rules → Stage B Installer Wizard + Storage Settings → Stage C Format Restore UI → Stage D 导出/全局搜索/备份恢复 → Stage E 三文件比对评估 → Stage F updater 与经授权的 Beta/Prerelease/Stable。

已完成 task 保存原阶段实现和验收证据，旧强制角色/轮次、独立 Quick Compare、页签布局或 Own 优先推荐不是后续产品约束，也不证明新需求完成。角色暂未指定、自动编号和自动项目保存的兼容方案需在新实现任务中验证，现有模型/迁移保留。Three-way 在 Beta 前评估 Spike，过大可明确延后 v0.1.x；PDF/OCR 仅未来规划。

Installer Wizard 是 Beta 前阻断项；当前开发 Setup 安装成功不能替代目录浏览/原地升级/卸载/数据保留验收。真实滚轮/触控板联动体验也不能只用 unit tests 代替。仅校准 Markdown 文档时检查范围、格式/相对链接、diff 后正常 commit/push，不触发安装包、Tag 或 Release，不修改业务代码、schema 或 workflow。

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

以下命令按需选择，不是每次任务都顺序运行的清单。首次准备环境或依赖变化时 restore；局部开发通常只需相关项目的 test（自动完成所需 build）：

```powershell
dotnet tool restore
dotnet restore FinalCheck.sln
dotnet test tests/FinalCheck.App.Tests/FinalCheck.App.Tests.csproj -c Release
dotnet run --project src/FinalCheck.Desktop/FinalCheck.Desktop.csproj
```

需要整库构建/回归时采用一轮 build + no-build test，避免重复构建。发布产物验证另按 [release process](release-process.md) 执行：

```powershell
dotnet build FinalCheck.sln --configuration Release
dotnet test FinalCheck.sln --configuration Release --no-build
```

当前 Release publish 会排除用户运行不需要的 `.pdb` 调试符号，但不启用 trimming 或 NativeAOT。

## 按风险验证标准

2026-10-15 用户确认采用 Karpathy Guidelines 的最小充分验证方案。以下规定执行频率，不降低 PRD 的验收目标，也不将历史未执行项改成通过。

| 本次改动 | 必要验证 | 不默认追加 |
| --- | --- | --- |
| 文档、开发规范或文案 | diff、相关链接/引用、必要格式或语法检查 | build、全量测试、Test Build、安装 |
| 局部业务/算法代码 | 相关单元/集成测试；修复缺陷时覆盖复现条件 | 无关模块、重复 Debug/Release 全量 |
| UI 行为、布局、导航 | 相关 VM/Headless 回归及受影响操作冒烟 | 所有页面、完整合同链、卸载重装 |
| 持久化、迁移、格式写回 | 真实隔离数据集成、原文/历史保留、失败恢复及受影响 UI | 无关功能或未改变的安装流程 |
| 跨模块改动或阶段交付 | 一轮适用的整库 build/test；UI 交付走相关用户路径 | 在同一实现上无理由重复同一轮检查 |
| 打包/安装/更新/卸载改动或安装包交付 | 包身份/完整性检查与对应实际生命周期验收 | 每个开发 Phase 都执行完整安装矩阵 |

优先复用现有自动测试、合成夹具和已通过证据，不另建通用验收框架。全量 Windows/macOS CI、版本脚本、包 verifier、发布确认与权限门禁保持现状；本规范不授权 Tag/Release，也不通过减少断言让失败通过。

真实滚轮/拖放/文件夹选择等只在相关交互改动或对应交付验收时检查，CLI 或 Headless 不能冒充真实输入。记录有设备/无设备、已执行/未执行；不因没有触控板而阻断无关功能开发。原始 DOCX、修订/批注、独立历史、迁移失败恢复等安全测试继续保留。

同一实现通过后，只有新增代码/配置影响、失败或未解决疑点才重跑相应检查。后续文档整理可复用此前二进制证据，注明构建 SHA 与文档提交；不因“最终 HEAD”包含纯文档而陷入重新打包/安装循环。交付仍应验证覆盖全部可执行改动的产物版本、SHA、Run ID；安全与安装器 Beta 门禁不得豁免。历史 task 的执行频率按本节解释，不删改历史证据或直接标记 DONE。

App 的真实 View / XAML Headless 测试使用 Avalonia 管理的程序集会话（`GetOrStartForAssembly`），显式声明应用入口和 `PerTest` 隔离；每个 Dispatch 仍创建并清理独立 Application，测试集合禁止并行。不要逐测试 `StartNew` / `Dispose`，Avalonia 12.1.2 的后台任务赋值存在会话销毁竞态，会在布局断言通过后导致测试包构建偶发失败。不得通过吞掉异常或移除正文、导航、虚拟化断言使测试通过。真实安装版交互门禁仍须单独执行，详见 [Stage A task](../tasks/usability-workflow-correction-v0.md)。

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

Comparison UI v0 已按 [task](../tasks/comparison-ui-v0.md) 接入入口/文件会话、后台执行、独立历史、结果清单、搜索/筛选/持久处理状态及原生双栏预览。架构见 [comparison-ui.md](../architecture/comparison-ui.md)。App.Tests 包含 VM 状态/导航、精确高亮、映射联动与 4000 段后台预览投影；Data.Tests 直接 link 同一 Desktop 工作流，使用 GUID 隔离 DOCX / SQLite 检查真实引擎、取消、Partial、冻结历史、review 并发保存、记录迁移及 20/400 段完整工作流。Database schema 4 只新增 ComparisonRecords，不改旧领域 payload schema。

交付 Comparison UI 时，使用覆盖全部可执行改动的 Windows Test Build，核对 Actions head_sha / Artifact version / Run ID、下载完整性并实际冒烟受影响的比较/审阅/重启路径；只追记文档不要求重打包。安装前保护既有数据；安装/升级/卸载与 Portable 启动矩阵用于相应打包生命周期改动或正式交付，不对每个 UI Phase 重复执行。代码修正影响已测路径时，才更新产物并复验。

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

## Stage A 工作台验证

跨设备人工验收样本可用已纳入 solution 的 `FinalCheck.AcceptanceFixtures` 重建：在仓库根目录运行 `./scripts/acceptance-fixtures.ps1`，输出纯合成 DOCX 与 hash 清单；使用 `-Action Verify -OutputDirectory '<绝对目录>' -NoBuild` 只读复核。样本目录、Top 3/版本链步骤、CLI 与真实安装验收的区别见 [跨设备合成验收夹具](acceptance-fixtures.md)。不复制本机数据库或用户合同，不将生成成功视作人工门禁通过。

开发主机的 2026-09-30 资源耗尽事件、.NET 后台 manifest / MSI 时间线及证据边界见 [只读排查记录](incidents/2026-09-30-memory-pressure.md)。原始机器日志仅保存在本机 Git 忽略的 artifacts，不提交合同、数据库或转储；该环境事件不是已经定性的业务缺陷。

先读当前 [Stage A task](../tasks/usability-workflow-correction-v0.md)、[校准 PRD](../PRD/PRD-v0.1.0.md) 和 [Comparison UI architecture](../architecture/comparison-ui.md)。复用已有审阅/Undo、筛选导航、逻辑 Anchor 和真实 View/XAML Headless 回归，按改动选择运行范围。只有改动滚动/拖放等交互或交付工作台时，再补对应真实输入检查，不把它作为无关任务的前置阻断。

Debug-only `--comparison-workspace-fixture` 必须同时提供 `--developer-data-directory <GUID 隔离绝对路径>`，在独立目录生成 400 段基准、508 段当前（含集中新增/删除、格式变化和合成批注）的 DOCX，并调用真实 Comparison 流程保存冻结历史。随后用同一隔离目录启动 Debug 应用，首页“继续最近一次比对”进入工作台，验证慢/快鼠标滚轮、滚动条拖动、换侧 Leader、联动开关、连续上一/下一项与 Undo/重启。不要在用户正常 DataRoot 上生成夹具或试验数据库迁移；不要把 Debug 试用当作最终 HEAD 安装包验收。

```powershell
$stageACheck = Join-Path $env:TEMP ('FinalCheck-StageA1-' + [guid]::NewGuid().ToString('N'))
dotnet run --project src/FinalCheck.Desktop -c Debug -- --developer-data-directory $stageACheck --comparison-workspace-fixture
dotnet run --project src/FinalCheck.Desktop -c Debug -- --developer-data-directory $stageACheck
```

第二条命令打开同一隔离 Debug 数据，用于按需检查受影响操作，不是每次任务的整套验收。一次性 Stage A FlaUI runner 已于 2026-10-15 移除；历史使用结果仍是当时证据，源码可从 Git 历史恢复，不再维护独立的全流程 GUI 脚本。合成 DOCX CLI 和正常 Headless/集成测试保留，开发窗口仍不冒充安装器生命周期验证。

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

Debug-only `--developer-data-directory` 必须显式绝对路径，拒绝正常 FinalCheck 数据目录及其父子目录；bootstrap 同时隔离至 `<directory>.bootstrap`，Release 包无此配置。它只用于启动/DI/迁移冒烟，不是正式 UI。底层服务调用方提供稳定 ContractVersion Guid、Snapshot/Comparison 与 Plan；外部编辑必须明确 preserve/regenerate，不自动猜测。当前数据库 schema 3 新增 restore history，旧 Snapshot/Comparison 保留。

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

管理 Phase 2 schema 6 新增项目及一级文件夹，模板 FK/标签/筛选分页与编辑身份保护由 ProjectTests / ProjectsTests 验证；100 项目性能测试只读取 20 行 metadata。迁移检查包含项目 bindings、标签及 metadata digest，既有 Snapshot / Comparison / restore 不重建。

Project / Template / Version Management 的当前范围及 Phase 状态见 [task](../tasks/project-template-version-management-v0.md)、[architecture](../architecture/project-template-version-management.md)。Phase 1 数据库 schema 5 增量新增模板/版本，旧 Snapshot / Comparison / review payload 不变；模板导入/重关联、schema 升级和 Storage 原文排除使用 GUID 隔离夹具验证。每阶段相关检查后 commit + push；导入交互变化或对应功能交付须实际完成 Template / Contract Version Drag & Drop，不能仅用 VM 输入测试替代，不为纯文档重跑。

运行时先验证 bootstrap，再由 `IDataRootProvider` / `DataRootDbContextFactory` 定位 SQLite；`IAppDataPathProvider` 仅作兼容 adapter。Windows 新用户路径为：

```text
%LOCALAPPDATA%\FinalCheck\Data\finalcheck.db
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
- `IDataRootProvider` / bootstrap、path policy、maintenance coordinator、session rebind、migration/usage 已接入；所有业务文件写入必须使用 managed scope 的 `IAppDataPathProvider`，且 scope 在操作完成后释放。长期 scope 会令迁移等待/超时，不强制丢弃事务。
- Storage Settings 在隔离固定磁盘/生成式有数据 SQLite 和 DOCX 中验证复制、WAL、SHA/数量/大小、重新打开/重解析、locator 提交与 crash 恢复；失败保护旧 root，成功默认保留旧数据，立即刷新 UI/统计。不能用用户库试迁移或直接剪切。
- Snapshot / Comparison / restore payload 当前包含在 SQLite，逻辑 payload 统计不能与 Database 物理大小重复累加；统计跟随 root generation。原始 DOCX 路径与 PRD 导出位置不变。
- Windows fixed-disk 路径、权限/空间、同步目录/网络/可移动/链接阻断属于 Infrastructure/Desktop；Core/Documents/Comparison/Data 不引入 Registry、Shell 等 API。安装器 Spike 未通过前不改发布流水线或升级 Velopack 依赖。

## Storage Foundation 开发验证

正式存储页从侧栏“设置”进入。显示当前数据位置/采样时间/唯一物理总计，数据库内 payload 独立列出并注明包含于 Database；选择或输入空目标、验证、明确确认后调用 Foundation 迁移，完成立即刷新。失败显示阶段与诊断，旧数据保留；恢复核验不能当作普通重试。迁移中等待操作或取消结束后才能离开/普通关闭。

Stage B App.Tests 覆盖确认失效、partial、取消/提交、失败/NeedsReview、统计失败和导航；真实 Data.Tests link 同一 ViewModel 并复用 DesktopStorageServices，验证有数据 SQLite、Working Copy、restore history 和同进程新 scope。Headless 在 840×520 下检查可滚动设置页与确认按钮；这些测试不代表文件夹 Browse、实际安装版或物理磁盘迁移矩阵已经通过。

```powershell
dotnet test -c Release --filter FullyQualifiedName~Storage
$storageCheck = Join-Path $env:TEMP ('FinalCheck-Storage-Check-' + [guid]::NewGuid().ToString('N'))
dotnet run --project src/FinalCheck.Desktop -c Debug -- --developer-data-directory $storageCheck --storage-info
dotnet run --project src/FinalCheck.Desktop -c Debug -- --developer-data-directory $storageCheck --storage-validate ($storageCheck + '.target')
dotnet run --project src/FinalCheck.Desktop -c Debug -- --developer-data-directory $storageCheck --storage-migrate ($storageCheck + '.target')
```

命令执行后输出目标 generation / 各路径 / usage，不进入正式 UI。迁移测试目标严格为隔离 root + `.target`；此 Debug source/target 的 Temp 例外不编译进 Release。生产 DataRoot 拒绝 Temp、network/removable、known sync 和链接；不以修改环境变量隔离正式 Portable（Windows known-folder API 未必读取该变量）。失败保留旧数据与 staging，不自动清理或再次覆盖非空目标。

`StorageRuntimeTests` 直接 link 同一 `DesktopStorageServices.cs` 测试 storage composition，避免 Core compatibility 依赖 Avalonia/Velopack Desktop；不是维护第二套启动算法。真实 Desktop 仍单独做 GUID sandbox 窗口冒烟。打包产物可由独立验证 harness 注入隔离平台接口并使用产物程序集验证同一 composition，不给 Release 主入口加隐藏路径参数；验收必须区分 harness / Debug 窗口与正式 Setup 主入口实测，不拿空 DB 冒充历史迁移。

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
