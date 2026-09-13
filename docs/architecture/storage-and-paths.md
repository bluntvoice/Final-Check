# Storage and Paths

Current project-management database schema 9 incrementally adds a durable deletion audit; domain Snapshot / Comparison / review schemas do not change. Relative managed cleanup paths survive DataRoot relocation, while absolute original paths remain excluded after project deletion. Storage migration validates deletion journal schema/identity/path digests and preserves unfinished cleanup records; cleanup uses the active generation lease and exact per-version locks, never install paths or recursive deletion.

Schema 8 adds immutable ProjectComparisons context alongside existing frozen ComparisonRecord/result schemas. Storage migration validates same-project version/Own-baseline membership, source Snapshot identities, current Own baseline and context row digests; template history references remain valid after project binding changes. Original DOCX exclusions and independent DataRoot remain unchanged.

Project management schema 7 adds ContractVersions / NegotiationRounds incrementally. Storage migration verifies version Snapshot/hash, original-source and duplicate identities plus row digests; both relinked paths and immutable original source paths are excluded from managed-data copying/usage. Original DOCX is never moved, copied or deleted by DataRoot migration.

- 日期：2026-09-13
- 状态：Storage Foundation Phase 1–5、Release build/test、Windows/macOS CI 和一次 Test Build / 实际 Release payload 隔离启动已验证，证据见 task。正式 Storage Settings UI 尚未实现。
- 决策：[ADR-0006](ADR-0006-install-location-and-data-root.md)。

## 两种位置、四类路径

> Application install location and application data location are independent concepts.
>
> User data must never be stored inside the replaceable application installation directory.

| 路径类别 | 归属与规则 |
|---|---|
| InstallRoot | 用户选择的安装位置；Velopack 管理 current、Update.exe、stub、manifest、packages 等安装/更新载荷，可替换/卸载，不放业务数据 |
| Bootstrap configuration | 系统默认配置位置的小型 locator / 迁移恢复控制信息，不依赖 SQLite，不位于安装目录 |
| DataRoot | SQLite、Snapshot/Comparison/restore payload、Working Copy、内部备份/恢复、应用缓存、设置、业务日志/Temp 和可迁移运行数据 |
| 用户外部文件 | 原始 DOCX 只保存绝对路径，不复制、不改写；手动导出位置按 PRD 第 48 章（默认当前修订合同同目录，可临时改选），不因 DataRoot 改变 |

InstallRoot 与 DataRoot 禁止相同或父子重叠，不能把业务数据搬进可卸载的安装根目录；安装 Wizard 和存储设置都需要双向验证。Bootstrap 是启动定位信息，不属于安装目录配置；不得以当前工作目录、exe 目录或 SQLite 内设置推导唯一 DataRoot。

## 已有实现与兼容入口

`PlatformAppDataPathProvider` 已成为 DataRoot generation session 的兼容 adapter；`DataRootDbContextFactory` 以 pooling=false 构造同一 scope 的数据库连接，Working Copy 使用 `WorkingCopies/<ContractVersionId:N>/restored.docx`。维护屏障等待既有 scope 释放并阻断新 scope；已释放 scope 的路径不能继续使用。Debug-only developer data override 是隔离测试工具，不是正式用户路径设置。

数据库 schema 4：保留 DocumentSnapshots（Snapshot schema 2）、ComparisonResults（schema 1）、RestoredWorkingCopies、FormatRestoreOperations（operation schema 1），新增独立 ComparisonRecords（record schema 1）引用冻结快照/结果并保存外部文件身份与 review states。新表同步加入迁移 digest / 引用验证和原文排除 / Comparison 逻辑占用。领域 JSON payload 在 SQLite 内；目前没有独立 Snapshot / Comparison 文件仓库。恢复 metadata / operation 中已有绝对 WorkingPath、TemporaryPath、BackupPath，以及 OriginalPath；WorkingPath 还需与托管目录精确匹配。

Storage Foundation 已引入 bootstrap 和 provider，保持旧用户原位置而非搬动数据。启动顺序须保持 **Velopack lifecycle hooks → bootstrap / 恢复检查 → DataRoot provider → 数据层 → UI**，安装/卸载 hooks 不打开数据库。

兼容规则：

- 新用户且确认不存在既有数据时，默认 `%LocalAppData%\FinalCheck\Data`。
- 无 bootstrap 但旧 `FinalCheck/finalcheck.db` / WorkingCopies / 已识别业务数据存在：使用 LegacyLayout 定位现有数据，不自动创建新空库，不偷偷搬到 Data 子目录；记录待显式迁移。
- 同时发现多套数据、残缺旧库或未知文件：诊断并要求安全恢复/确认，不以“缺少库文件”冒充首次启动。
- 有 bootstrap 但目录不可达、SQLite 缺失、RootId 不符、配置损坏或版本未知：停止正常数据初始化并展示恢复入口，不回退默认目录建空库，不通过启动时自动 EF migration 猜测修复。
- 旧 root 恰好是 bootstrap 的父目录；迁移使用明确业务清单，排除 locator、locator backup、迁移控制文件和未知用户文件。通用迁移拒绝 old/new 父子重叠；从旧布局直接迁到其 Data 子目录需要另行验证的布局升级，不复用递归复制。旧用户可显式迁到不重叠的 D/E 盘目录。

## DataRoot 布局与统计

管理 Phase 2 schema 6 新增 Projects / ProjectFolders，project tags 为受校验 metadata JSON。项目模板/版本关系及 metadata digest 进入迁移验证；schema 5 及此前数据增量保留。Project list 不读取 Snapshot payload，Storage 全量验证仍核对历史完整性。

Project / Template / Version Management Phase 1 的数据库 schema 5 增量新增 Templates / TemplateVersions，保留旧表与 payload。模板 Snapshot 仍在 DocumentSnapshots，原 DOCX 路径加入 migration / usage 排除；新表行身份/digest、Snapshot/hash 与 FK 一起验证，迁移不改模板源路径。后续 Phase 扩展见 [管理架构](project-template-version-management.md)。

后续布局保留 `finalcheck.db` 与 `WorkingCopies` 名称，避免无必要同时改名：

```text
DataRoot/
  finalcheck.db                   SQLite 与其运行期间 WAL/SHM
  WorkingCopies/<version-guid>/   当前 restored.docx、内部 candidate/backup
  Backups/                       通用内部 backup / recovery（未来）
  Cache/                         可重建应用缓存（未来）
  Logs/                          应用日志（未来）
  Temp/                          受管理临时文件（未来）
  Settings/                      可迁移应用设置（未来）
```

这是布局契约，不要求本轮创建空目录。若将来外置 Snapshot / Comparison payload，须增加存储格式/version、manifest 和完整迁移，不把已有 JSON 全量搬出 SQLite 当作小型路径改动。

“设置 → 存储”显示当前 DataRoot、采样时间、总占用、打开文件夹、更改位置，以及 Database / Snapshot / Comparison / Working Copy / Backup / Cache / Logs / Temp；保留 PRD 的项目数据/历史语义，恢复记录可单独展示为 Database 细分项。

- 总占用按新 root 的实际文件唯一计数，包含数据库及 WAL/SHM，扫描不跟随链接；不将 InstallRoot updater packages、原始 DOCX 或手动导出文件加进 DataRoot 总计。
- Snapshot / Comparison / restore 目前在 DB 内，用 `length(Payload)` 等提供**逻辑 payload 占用**，明确显示“包含于 Database”，不能再次加到物理总计或宣称精确磁盘分摊。DB 页、索引、空闲页/WAL 与 payload 字节不一一对应。
- Working Copy 是当前工作文件；同目录 previous backup 归 Backup，candidate 归 Temp，锁/metadata 归运行开销，路径只计入一个物理桶。未知但受管理的文件显示 Other / Unclassified，不能隐去或删除来凑统计。
- 数据层切换成功后失效旧 root 的统计/cache、更新 RootGeneration，重新扫描新 root 并通知 ViewModel，无需重启；无法扫描显示原因/部分统计，不显示 0 假成功。

Velopack 自管 updater cache / `%LocalAppData%\velopack` 日志不是合同数据；当前上游位置尚未可配置，见安装器 Spike。业务缓存/日志仍必须在 DataRoot，不将上游限制当作业务目录耦合的例外。

## Bootstrap

Windows 固定 locator 位置：`%LocalAppData%\FinalCheck\bootstrap.json`。macOS 的默认配置/数据位置由现有平台边界的实现选择，核心层不能拼 Windows 路径。实际 schema 1 示意：

```json
{
  "schemaVersion": 1,
  "current": { "path": "D:\\FinalCheckData", "rootId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "layoutVersion": 1, "generation": 1 },
  "lastKnownGood": { "path": "D:\\FinalCheckData", "rootId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "layoutVersion": 1, "generation": 1 },
  "databaseInitialized": true
}
```

schema/version 用于验证 locator 和布局，不等同于数据库、Snapshot 或 restore schema。RootId / generation 防止使用错误目录或旧连接；bootstrap 只保存启动前必需信息，不保存合同正文、Snapshot、Comparison、用户原始文档列表或业务历史。默认配置位置保留 previous locator、独立 migration journal/清单与协作锁用于启动恢复；journal 是 operation 控制信息，业务备份在 DataRoot / staging，不放进 locator JSON。

写入先在同一配置目录创建唯一 temp、flush、验证，再通过平台原子 replace/rename 更新 locator，保留 previous。更新需 expected generation、互斥与读回，防止两个实例并行改位置；读回不确定不得直接认定成功或重试覆盖。不能把 DataRoot 只存于目标 SQLite。

## 基础接口与实施状态

Storage Foundation v0 Phase 1–5 已实现定位、统一路径、`IDataRootValidator`、`IStorageMaintenanceCoordinator` / generation session、`IDataRootMigrationService`、启动 recovery 和 `IStorageUsageService`。准确验收状态见 [storage task](../tasks/storage-foundation-v0.md)。基础服务已接入不等于正式 UI / Installer 已完成。

bootstrap schema 1 实际字段为 `schemaVersion`、`current` / `lastKnownGood` descriptor 和 `databaseInitialized`；descriptor 包含 path / rootId / layoutVersion / generation。DataRoot 内 `.finalcheck-root.json` 用于身份核验。LastKnownGood 是**当前已提交 generation** 的可信定位，不是“永远选择迁移前旧库”；`bootstrap.json.previous` 仅保留审计材料。首次注册允许尚未初始化数据库，初始化完成必须标记，之后缺库明确阻断。旧布局自动识别不搬动 payload / Working Copy。

接口与纯记录放 Core；Windows 路径/磁盘/Registry/folder-open 实现放 Infrastructure/Desktop；SQLite backup/integrity 放 Data。以下为职责契约，具体签名在 Storage Foundation / 后续 Storage Settings 任务中和测试一起落地：

| 接口 | 必要职责 |
|---|---|
| `IPlatformStoragePaths` | 获取默认配置位置、默认数据位置及安装上下文；不由 Data 查询 OS/Registry |
| `IStorageBootstrapStore` | Load/Validate、expected-generation 原子保存、previous/journal 恢复；不依赖目标数据库 |
| `IDataRootProvider` | 提供不可变 `DataRootDescriptor(Path, RootId, LayoutVersion, Generation)`；业务调用读一个 generation，不公开任意 SetPath |
| `IDataRootValidator` | 规范化、磁盘类型、同步目录风险、权限/空间/重叠验证，返回明确诊断 |
| `IStorageMaintenanceCoordinator` | 排空并阻止 DB/Working Copy/backup/cache/log 等新写入、跨进程互斥和 root generation lease；不是仅暂停 UI 按钮 |
| `IDataRootMigrationService` | 复制/验证 → 预备新数据层 → 提交/切换；独立 `StorageMigrationRecoveryService` 负责 operation recovery，结果带阶段/错误/旧数据保留位置 |
| `DataRootDbContextFactory` / `IStorageDataSession` | 每个 scope 持有一个 descriptor 与 lease，维护排空后准备新 generation，pooling=false 不复用旧连接 |
| `IStorageUsageService` | 带 root generation 的物理/逻辑统计和刷新，不 double-count |
| `IStorageRootChangeNotifier` | 提交成功、屏障释放后通知当前进程；刷新 handler 失败只诊断，不回滚已提交 DataRoot |

`IAppDataPathProvider` 已作为兼容 adapter，从 descriptor 派生数据库/WorkingCopies 路径，而不再独立决定另一根目录。数据库写入者和文件写入者必须共同遵守 lease，生命周期 hook 不进入迁移服务。单独添加可变 provider 而不解决已有 scope/绝对引用/并发，不能算安全基础实现。

## 非破坏性迁移算法

正常流程的操作状态为 Planned → Quiesced → Copied → Validated → ReadyToCommit → Committed；失败记录具体 stage，未知状态进入 NeedsReview。operation record 至少有 OperationId、old/new RootId/path/generation、layout version、阶段/时间、manifest 摘要、验证结果、失败原因及 locator 提交状态，不记录合同正文。

1. 用户选择新绝对目录；计算规范真实路径，按路径政策验证，显示范围和旧数据保留原则。只接受空目标或同 OperationId/RootId 的可恢复 staging，不能合并覆盖另一套数据库。
2. 验证当前用户创建、写入、flush、重命名权限（唯一 probe）；检查空间足够容纳一致性数据库备份、完整受管理文件、必要 manifest 和安全余量。空间无法可靠获取则拒绝；中途仍检查 ENOSPC，不依赖一次估算保证。
3. 获取全局/跨进程维护屏障，阻止所有新数据与托管文件写入；等待已开始事务/restore 完成，要求其他实例和 Word/WPS 关闭，处理可验证的 Prepared restore journal。未知 hash 或未解决 journal 先停止迁移并诊断，不自动接受修订、覆盖外部编辑或丢弃 backup。
4. 排空旧 DbContext、读写流与对应连接池。建立有版本的完整受管理清单：路径、类别、大小、SHA-256、数量和必要业务 identity；不跟随链接、不复制原始 DOCX/导出、不遗漏内部 recovery 材料。旧数据保持原位，缓存/Temp/业务日志也完整复制，不能默默以“可重建”为由丢失。
5. 在目标同一磁盘的唯一 staging（如同级 `.FinalCheck-migration-<id>`）建立一致性 SQLite backup，再复制全部其他受管理数据。活动 DB 不采用仅复制 .db 的方式，详见 SQLite 策略。复制流 flush，取消只在提交前生效，保留旧 root；只处理本操作拥有的 staging。
6. 核对 staging 文件数量、大小与 SHA-256、数据库完整性/外键/schema、各表 ID/行数和 payload digest。数据库 backup 的物理页排列可不同，不能强求源活跃 .db 的 binary SHA 等于 backup；记录 backup SHA 验证其后续传输，用逻辑 digest 验证同一业务事实。
7. 仅在目标库内执行经过显式版本测试的托管引用重定位/兼容转换，并记录 old→new 路径映射及转换前后 payload hash。优先新存储引用为 DataRoot-relative + RootId；旧绝对路径必须验证确在托管 root 内，不能字符串全局替换 JSON。OriginalPath、外部导出路径、文档内容 SHA、Snapshot/Comparison identity、操作/Undo 链保持原语义；历史原 payload 在保留的旧库及迁移备份中可审计。未知 schema/路径不一致拒绝切换。
8. 将验证后的 staging 在目标磁盘内 finalize 为选定 DataRoot（不覆盖既有目录），用隔离的新 session 重新打开目标库，加载 Snapshot/Comparison/restore 历史、核对 managed metadata、重解析 Working Copy 并验证 SHA/修订/批注、验证必要 Undo/recovery 引用。旧 root 此时仍是有效来源，新 session 不接受用户写入。
9. 预备完整新服务 session / provider descriptor / UI 统计，保持写屏障；所有可失败的数据层初始化和完整性检查必须在提交前完成。写 durable ReadyToCommit journal，复查 locator generation 和源/目标 manifest 未被外部改动。
10. 在极短、不响应取消的提交区原子更新并读回 bootstrap，然后发布已预备的 in-memory session/descriptor，正式切换、释放写屏障、通知 UI/统计刷新；新写入只能进入新 generation，不能让旧 scope 继续写。成功返回新位置和“旧数据已保留”，不自动删除旧 root。

迁移失败显示 stage、可理解原因、已保留位置和重试入口；旧数据从不剪切/删除。提交前任一失败保持旧 locator/root 生效，恢复旧 session 后解除屏障；残留目标标为本操作未完成，重试先核对 manifest，不能直接覆盖。若提交期间进程仍运行但切换失败，在新写入尚未释放前核实 durable locator，安全恢复 previous locator/旧 session，否则进入锁定恢复，不能猜测或产生双写。

**崩溃与提交边界：** 文件、SQLite、bootstrap 不是共同事务。bootstrap 尚未提交时启动沿用旧 root；已经持久提交且新库已验证时，启动从新 root 完成 Committed journal 和切换恢复，而不是静默退回旧库产生分叉。无法确认提交、new/old 均被外部更改、配置读回失败时阻止写入、明确 NeedsReview。提交后单纯 UI 刷新错误记录并重试刷新，不误报迁移失败后将写入切回旧库。必须故障注入验证断电/异常落在每个边界的结果，不承诺不存在的跨资源绝对原子性。

旧位置后续清理是单独、用户明确确认的流程，须展示路径/占用/外部原文引用影响，确认不是当前 DataRoot、bootstrap 或安装位置；迁移本身没有自动删除动作，不在本轮实现清理。

## SQLite 安全策略

Phase 4 实现采用源库空 IMMEDIATE 事务阻断外部 SQLite 写入，独立只读连接调用 BackupDatabase；数据库上下文关闭/释放后以新 root 重开验证。托管文件复制前后完整 SHA 清单、目标精确数量、旧库逻辑 digest 和 Working Copy 重解析共同保证一致性；不以空库 integrity 代替历史验收。`storage-session.lock` 协调合作进程的 scope，迁移等待 10 秒超时或取消则保护旧会话。所有新增业务文件写入者必须沿用该 scope lease；外部 Word/WPS 仍须关闭。

现有 restore JSON 绝对路径采用强类型、精确布局验证的 old→new 转换，不全局替换字符串；当前领域 schema / layout 未改变。源库与新 root `Backups/storage-migration-<id>/source-finalcheck.db` 保存原始历史，可审计；将来相对引用格式升级必须另行版本化。新 descriptor 可在维护屏障内预备发布，用户写入只在 durable bootstrap 验证后释放，提交前异常恢复旧 descriptor。残留 staging / finalize 目录默认保留，自动续传和旧目录清理未实现。

- SQLite 主库仅支持经过政策验证的本机固定磁盘；NAS/SMB/网络共享不在首阶段支持范围。SQLite WAL 依赖同机共享内存/锁，不能以“目录可写”证明网络存储可靠。
- 首选已有 Microsoft.Data.Sqlite 的 `SqliteConnection.BackupDatabase` 做一致性备份，配合全应用维护屏障保护数据库与 Working Copy/history 的共同状态。备份 API 的数据库写锁不能替代文件/后台写入屏障。
- 不在连接活动时只复制 `finalcheck.db` 或忽略未 checkpoint 的 WAL；不得直接删除 WAL/SHM。若选择关闭连接后的 raw copy 备选方案，必须证明 WAL 已完成 checkpoint、没有其他进程持有连接，并测试 busy/crash 边界，否则不用。
- 验证 `PRAGMA integrity_check`、`foreign_key_check`、migration/schema 支持、表/主键集合/行数/payload hash 与 Working Copy identity；测试有真实生成数据和未 checkpoint WAL 的库，不用空表代替验收。
- 路径迁移不顺带执行产品 schema 升级；bootstrap/layout 与领域 schema 版本分离。确需 managed-reference 存储格式升级时，单独 versioned transform、备份与兼容测试，失败不写源库。
- 多进程维护锁和 root generation 防止另一实例沿旧 bootstrap 写库；外部程序不遵循应用锁，要求关闭并校验 hash，异常则拒绝切换。当前 per-version operation.lock 不能视为已实现全局迁移锁。

官方依据：[Microsoft.Data.Sqlite online backup](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup)、[SQLite Backup API](https://www.sqlite.org/backup.html)、[SQLite WAL constraints](https://www.sqlite.org/wal.html)（访问日期 2026-09-12）。

## Runtime / 启动与统计实施

`DesktopStorageServices` 是真实 Desktop 的 storage composition：resolver 在创建数据服务前核验 bootstrap、RootId、只读数据库以及固定盘 / Temp / known sync 政策；startup recovery 完成后才初始化已知 EF schema 并标记 DatabaseInitialized，最后进入 Avalonia。产品数据库升级与目录迁移仍分离。失败写最小配置位置诊断并停止，不建空库。

未提交中断 journal 以明确 `InterruptedBeforeCommitOldDataRetained` 结束该次失败尝试，保留 staging / finalize，允许用户选择新空目标重试；不能一直留下过时 InProgress 记录，在重试成功后阻断新 generation。已提交 receipt 在当前新 root 验证并恢复，允许保留新 root 中后续合法写入；未知状态阻断写入。

StorageUsage 按需计算，不后台轮询或路径 getter 扫盘；扫描期间持有 generation session，不跟随链接。SQLite payload 以只读事务计算 length，Snapshot / Comparison / restore 逻辑 bytes 明示包含于 Database；物理 Total 仅为唯一文件分类之和（DB+WAL/SHM、当前 Working Copy、previous/通用 Backup、Cache、Logs、candidate/Temp、Other）。排除已记录原始 DOCX 与 legacy-root bootstrap-control，扫描/数据库失败返回 partial diagnostics，不能显示 0 假成功。迁移后同一个服务实例读新 root，事件通知可供未来 ViewModel 刷新，无需退出进程。

Debug-only `--storage-info` / `--storage-validate` / `--storage-migrate` 必须与显式隔离 developer root 使用；测试迁移目标限定 `<developer-root>.target`，只有该隔离 source/target 可绕过生产 Temp 限制，磁盘类型/known sync 等政策仍保留。Release 不编译 developer 类型和命令，也没有环境变量/隐藏参数数据路径 override。

## v0.1.0 路径政策

Storage Foundation Phase 3 已实现 `IDataRootValidator`：由 Infrastructure 的 `DriveInfo` / known OneDrive 环境根提供磁盘 facts，目录权限以唯一 probe 的创建、flush、读回、rename 验证，不改 ACL。实际安装根由 Desktop 的固定版本 Velopack locator 注入；Source overlap 和非空 target 默认阻断。预算为 source physical bytes + 一致性 DB 额外 backup bytes + max(64 MiB, source/10)；未知空间/类型失败阻断。生产 validator 不放开 Temp 或 network，测试仅使用注入 facts 的 GUID 隔离夹具。未知同步工具不保证检测。

正式首阶段只允许 Windows 当前用户可读写、可重命名的本机固定磁盘绝对目录。规范化后校验每个现有祖先/目标、真实磁盘类型、ACL 和所有权；固定 D/E 盘正常可支持，不要求管理员或限定 C 盘。

- 拒绝相对路径、盘根、设备路径/危险名称、网络 UNC（含扩展 UNC）与映射网络盘、可移动盘、已识别 OneDrive 等同步根目录、symlink/junction/reparse-point，以及安装/数据根重叠、old/new root 互为父子、其他非空数据目录。
- 已知同步目录采用阻止而非“已支持但自担风险”；仅检查 DriveType.Fixed 不足以识别同步目录。平台 policy 结合 known sync roots / cloud attributes 等检测，无法识别所有第三方同步工具时明确限制，不能声称可靠支持。
- 权限不足、剩余空间不足、未知文件系统/无法验证路径能力显示明确阻断原因；不自动提升权限、修改 ACL 或切换到另一目录。
- 后续 NAS/SMB/同步/可移动支持需要单独可靠性 Spike（SQLite locking/WAL、断线、重连、文件原子操作、同步冲突和恢复），不随普通 folder picker 上线而宣布支持。

## 后续 Storage Settings 验收（未执行）

必须覆盖新用户 bootstrap / 旧布局兼容、有数据迁移 D/E 盘、路径风险/权限/空间、配置损坏/丢库不建空库、SQLite WAL 一致性、managed path/history/Undo/recovery、原始与导出路径不变、跨进程/外部编辑、取消、每步 I/O/数据库故障与提交前后 crash、旧数据保留，以及新路径立即生效/统计无重复计数。接口纯契约与已有核心测试仍保持 macOS 编译；Windows folder UI/磁盘/锁/原子替换实测独立记录。本轮文档测试和旧引擎回归不能冒充这些新功能验收。
