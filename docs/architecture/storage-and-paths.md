# Storage and Paths

- 日期：2026-09-12
- 状态：正式需求与接口设计已确定；DataRoot / bootstrap / Storage Settings / 迁移功能尚未实现。
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

当前 `PlatformAppDataPathProvider` 返回 `%LocalAppData%\FinalCheck`，数据库为其下 `finalcheck.db`；`FormatRestoreWorkingCopyService` 使用 `WorkingCopies/<ContractVersionId:N>/restored.docx`。Desktop 每个 DbContext scope 从 `IAppDataPathProvider` 生成连接字符串，但旧 scope/连接池不会随路径自动失效。Debug-only developer data override 是隔离测试工具，不是正式用户路径设置。

数据库 schema 3：DocumentSnapshots（Snapshot schema 2）、ComparisonResults（schema 1）、RestoredWorkingCopies、FormatRestoreOperations（operation schema 1），领域 JSON payload 在 SQLite 内；目前没有独立 Snapshot / Comparison 文件仓库。恢复 metadata / operation 中已有绝对 WorkingPath、TemporaryPath、BackupPath，以及 OriginalPath；WorkingPath 还需与托管目录精确匹配。

因此本轮不直接替换 provider、创建 bootstrap 或改变默认数据库路径。后续启动顺序须保持 **Velopack lifecycle hooks → bootstrap / 恢复检查 → DataRoot provider → 数据层 → UI**，安装/卸载 hooks 不打开数据库。

兼容规则：

- 新用户且确认不存在既有数据时，默认 `%LocalAppData%\FinalCheck\Data`。
- 无 bootstrap 但旧 `FinalCheck/finalcheck.db` / WorkingCopies / 已识别业务数据存在：使用 LegacyLayout 定位现有数据，不自动创建新空库，不偷偷搬到 Data 子目录；记录待显式迁移。
- 同时发现多套数据、残缺旧库或未知文件：诊断并要求安全恢复/确认，不以“缺少库文件”冒充首次启动。
- 有 bootstrap 但目录不可达、SQLite 缺失、RootId 不符、配置损坏或版本未知：停止正常数据初始化并展示恢复入口，不回退默认目录建空库，不通过启动时自动 EF migration 猜测修复。
- 旧 root 恰好是 bootstrap 的父目录；迁移使用明确业务清单，排除 locator、locator backup、迁移控制文件和未知用户文件。通用迁移拒绝 old/new 父子重叠；从旧布局直接迁到其 Data 子目录需要另行验证的布局升级，不复用递归复制。旧用户可显式迁到不重叠的 D/E 盘目录。

## DataRoot 布局与统计

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

Windows 固定 locator 位置：`%LocalAppData%\FinalCheck\bootstrap.json`。macOS 的默认配置/数据位置由现有平台边界的实现选择，核心层不能拼 Windows 路径。示意契约（非本轮生成的文件）：

```json
{
  "bootstrapSchemaVersion": 1,
  "storageLayoutVersion": 1,
  "dataRoot": "D:\\FinalCheckData",
  "dataRootId": "<stable-root-guid>",
  "generation": 1
}
```

schema/version 用于验证 locator 和布局，不等同于数据库、Snapshot 或 restore schema。RootId / generation 防止使用错误目录或旧连接；只保存启动前必需信息，不保存合同正文、Snapshot、Comparison、用户原始文档列表或业务历史。可在默认配置位置保留小型 previous locator、迁移 journal/锁用于启动恢复；完整业务备份与迁移清单归 DataRoot / staging。

写入先在同一配置目录创建唯一 temp、flush、验证，再通过平台原子 replace/rename 更新 locator，保留 previous。更新需 expected generation、互斥与读回，防止两个实例并行改位置；读回不确定不得直接认定成功或重试覆盖。不能把 DataRoot 只存于目标 SQLite。

## 基础接口与实施状态

Storage Foundation v0 Phase 1 已实现 Core `IDataRootProvider` / 不可变 `DataRootPaths`、平台 `IPlatformStoragePaths`、文件 `IStorageBootstrapStore` 和 Data 只读数据库 inspector，尚未接入 Desktop 或实现迁移。准确阶段状态见 [storage task](../tasks/storage-foundation-v0.md)。以下其余职责仍是待实现契约，不把模型存在当作热切换已完成。

bootstrap schema 1 实际字段为 `schemaVersion`、`current` / `lastKnownGood` descriptor 和 `databaseInitialized`；descriptor 包含 path / rootId / layoutVersion / generation。DataRoot 内 `.finalcheck-root.json` 用于身份核验。LastKnownGood 是**当前已提交 generation** 的可信定位，不是“永远选择迁移前旧库”；`bootstrap.json.previous` 仅保留审计材料。首次注册允许尚未初始化数据库，初始化完成必须标记，之后缺库明确阻断。旧布局自动识别不搬动 payload / Working Copy。

接口与纯记录放 Core；Windows 路径/磁盘/Registry/folder-open 实现放 Infrastructure/Desktop；SQLite backup/integrity 放 Data。以下为职责契约，具体签名在 Storage Foundation / 后续 Storage Settings 任务中和测试一起落地：

| 接口 | 必要职责 |
|---|---|
| `IPlatformStoragePaths` | 获取默认配置位置、默认数据位置及安装上下文；不由 Data 查询 OS/Registry |
| `IStorageBootstrapStore` | Load/Validate、expected-generation 原子保存、previous/journal 恢复；不依赖目标数据库 |
| `IDataRootProvider` | 提供不可变 `DataRootDescriptor(Path, RootId, LayoutVersion, Generation)`；业务调用读一个 generation，不公开任意 SetPath |
| `IStoragePathPolicy` | 规范化、磁盘类型、同步目录风险、权限/空间/重叠验证，返回明确诊断 |
| `IStorageMaintenanceCoordinator` | 排空并阻止 DB/Working Copy/backup/cache/log 等新写入、跨进程互斥和 root generation lease；不是仅暂停 UI 按钮 |
| `IStorageMigrationService` | Analyze → 复制/验证 → 预备新数据层 → 提交/切换 → operation recovery；返回阶段、结果、错误与旧数据保留位置 |
| `IDataRootSessionFactory` | 针对指定 descriptor 新建受隔离的数据 scope；预验证新库、准备切换、释放旧上下文/对应连接池，不复用旧连接 |
| `IStorageUsageService` | 带 root generation 的物理/逻辑统计和刷新，不 double-count |

`IAppDataPathProvider` 后续作为迁移兼容 adapter，从 descriptor 派生数据库/WorkingCopies 路径，而不再独立决定另一根目录。数据库写入者和文件写入者必须共同遵守 lease，生命周期 hook 不进入迁移服务。单独添加可变 provider 而不解决已有 scope/绝对引用/并发，不能算安全基础实现。

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

- SQLite 主库仅支持经过政策验证的本机固定磁盘；NAS/SMB/网络共享不在首阶段支持范围。SQLite WAL 依赖同机共享内存/锁，不能以“目录可写”证明网络存储可靠。
- 首选已有 Microsoft.Data.Sqlite 的 `SqliteConnection.BackupDatabase` 做一致性备份，配合全应用维护屏障保护数据库与 Working Copy/history 的共同状态。备份 API 的数据库写锁不能替代文件/后台写入屏障。
- 不在连接活动时只复制 `finalcheck.db` 或忽略未 checkpoint 的 WAL；不得直接删除 WAL/SHM。若选择关闭连接后的 raw copy 备选方案，必须证明 WAL 已完成 checkpoint、没有其他进程持有连接，并测试 busy/crash 边界，否则不用。
- 验证 `PRAGMA integrity_check`、`foreign_key_check`、migration/schema 支持、表/主键集合/行数/payload hash 与 Working Copy identity；测试有真实生成数据和未 checkpoint WAL 的库，不用空表代替验收。
- 路径迁移不顺带执行产品 schema 升级；bootstrap/layout 与领域 schema 版本分离。确需 managed-reference 存储格式升级时，单独 versioned transform、备份与兼容测试，失败不写源库。
- 多进程维护锁和 root generation 防止另一实例沿旧 bootstrap 写库；外部程序不遵循应用锁，要求关闭并校验 hash，异常则拒绝切换。当前 per-version operation.lock 不能视为已实现全局迁移锁。

官方依据：[Microsoft.Data.Sqlite online backup](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup)、[SQLite Backup API](https://www.sqlite.org/backup.html)、[SQLite WAL constraints](https://www.sqlite.org/wal.html)（访问日期 2026-09-12）。

## v0.1.0 路径政策

正式首阶段只允许 Windows 当前用户可读写、可重命名的本机固定磁盘绝对目录。规范化后校验每个现有祖先/目标、真实磁盘类型、ACL 和所有权；固定 D/E 盘正常可支持，不要求管理员或限定 C 盘。

- 拒绝相对路径、盘根、设备路径/危险名称、网络 UNC（含扩展 UNC）与映射网络盘、可移动盘、已识别 OneDrive 等同步根目录、symlink/junction/reparse-point，以及安装/数据根重叠、old/new root 互为父子、其他非空数据目录。
- 已知同步目录采用阻止而非“已支持但自担风险”；仅检查 DriveType.Fixed 不足以识别同步目录。平台 policy 结合 known sync roots / cloud attributes 等检测，无法识别所有第三方同步工具时明确限制，不能声称可靠支持。
- 权限不足、剩余空间不足、未知文件系统/无法验证路径能力显示明确阻断原因；不自动提升权限、修改 ACL 或切换到另一目录。
- 后续 NAS/SMB/同步/可移动支持需要单独可靠性 Spike（SQLite locking/WAL、断线、重连、文件原子操作、同步冲突和恢复），不随普通 folder picker 上线而宣布支持。

## 后续 Storage Settings 验收（未执行）

必须覆盖新用户 bootstrap / 旧布局兼容、有数据迁移 D/E 盘、路径风险/权限/空间、配置损坏/丢库不建空库、SQLite WAL 一致性、managed path/history/Undo/recovery、原始与导出路径不变、跨进程/外部编辑、取消、每步 I/O/数据库故障与提交前后 crash、旧数据保留，以及新路径立即生效/统计无重复计数。接口纯契约与已有核心测试仍保持 macOS 编译；Windows folder UI/磁盘/锁/原子替换实测独立记录。本轮文档测试和旧引擎回归不能冒充这些新功能验收。
