# Final Check — Storage Foundation v0

> Status: IN PROGRESS  
> Version: v0.1.0 development  
> Scope: DataRoot / Bootstrap / Storage Migration Foundation  
> Depends on: Document Engine v0, Comparison Engine v0, Format Restore Engine v0  
> Last updated: 2026-09-12

---

## 1. 任务目标

将已经确认的 Storage / DataRoot 架构正式落地为生产级基础设施。

本阶段只解决：

> Final Check 的业务数据究竟存在哪里，以及程序如何安全地定位、切换和迁移 DataRoot。

本阶段完成后：

- 业务数据路径不再硬编码为固定 `%LocalAppData%`；
- SQLite、Snapshot、Comparison、Format Restore Working Copy 等核心数据能够通过统一 DataRoot 定位；
- 程序能够通过轻量 bootstrap 找到当前 DataRoot；
- 后续 Storage Settings UI 可以直接调用已经验证过的底层服务；
- 为用户把业务数据迁移到 D/E 盘提供正式能力。

本阶段不做完整 Storage Settings UI。

---

# 2. 开始前必须读取

执行前重新读取：

1. `AGENTS.md`
2. `docs/tasks/storage-foundation-v0.md`
3. `docs/PRD/PRD-v0.1.0.md`
4. `docs/architecture/storage-and-paths.md`
5. `docs/architecture/installer-architecture.md`
6. `docs/architecture/ADR-0006-install-location-and-data-root.md`
7. `docs/architecture/ADR-0002-document-snapshot-schema-and-persistence.md`
8. `docs/architecture/ADR-0005-format-restore-working-copy-and-undo.md`
9. 当前 Data / Infrastructure / Desktop / Documents / Comparison / Format Restore 相关代码
10. 当前数据库路径和文件路径实现

不得根据聊天记忆推测当前实际实现。

---

# 3. 多设备与 Phase 规则

严格遵守 `AGENTS.md`。

每完成一个 Phase：

1. 实现；
2. 测试；
3. 更新 task 状态；
4. 独立 commit；
5. 正常 push；
6. push 成功后进入下一 Phase。

---

# 4. 本阶段明确不做

不扩大到：

- 完整 Storage Settings 页面
- 安装目录选择 Wizard
- 网络盘支持
- 云同步目录支持
- 可移动盘支持
- 完整 Backup / Restore UI
- Comparison UI
- Stable / Prerelease Release
- macOS 安装包

允许创建 Debug / Developer 验证入口。

---

# 5. 总体原则

必须长期满足：

> InstallRoot ≠ DataRoot

程序安装目录只存程序文件。

业务数据不得写入可被升级替换的安装目录。

用户原始 DOCX：

> 继续只记录原路径，不纳入 DataRoot。

---

# 6. Phase 1 — DataRoot Provider / Bootstrap

## 6.1 IDataRootProvider

建立正式抽象，例如：

`IDataRootProvider`

至少提供：

- CurrentDataRoot
- DatabasePath
- SnapshotPath / Payload location
- WorkingCopyPath
- BackupPath
- CachePath
- LogPath
- TempPath

具体目录层级可以根据 architecture 文档确定。

不要让不同模块自行拼：

`%LocalAppData%\FinalCheck\...`

---

## 6.2 Bootstrap

建立轻量 bootstrap 存储。

Windows 建议：

`%LocalAppData%\FinalCheck\bootstrap.json`

只保存启动前必要信息，例如：

- SchemaVersion
- DataRoot
- LastKnownGoodDataRoot
- 必要 recovery metadata

不得把业务项目数据放进 bootstrap。

---

## 6.3 Bootstrap 原子写入

bootstrap 修改必须：

```text
write temp
→ validate
→ atomic replace
```

防止写到一半损坏后程序无法启动。

---

## 6.4 默认 DataRoot

新用户默认：

`%LocalAppData%\FinalCheck\Data`

但必须通过 Provider 获取。

---

## 6.5 旧用户兼容

这是硬性要求。

如果当前用户在旧版本已经存在：

`%LocalAppData%\FinalCheck\finalcheck.db`

或现有历史目录结构，

首次引入 bootstrap 时：

不得因为没有 `bootstrap.json` 就直接创建一个空的新数据库并表现为“数据全部消失”。

必须：

1. 检测历史数据；
2. 判断其是否属于合法 Final Check 数据；
3. 自动生成指向兼容位置的 bootstrap；
4. 保留现有数据。

如果旧布局需要内部规范化：

优先延迟迁移，而不是静默搬动。

---

## Phase 1 验收

至少覆盖：

- 全新用户；
- 已存在旧数据库；
- bootstrap 不存在；
- bootstrap 正常；
- bootstrap JSON 损坏；
- DataRoot 不存在；
- LastKnownGood fallback；
- Windows 路径；
- Core 不引入 Windows API。

完成后：

`Phase 1 — DONE`

建议 commit：

`feat: add data root bootstrap foundation`

---

# 7. Phase 2 — Storage Path Adoption

将现有业务数据路径逐步统一到 DataRoot。

至少检查：

- SQLite
- Snapshot persistence
- Comparison persistence
- Format Restore Working Copy
- Restore history / journal
- Internal backup
- Cache
- Logs / temp（适合迁移的部分）

---

## 7.1 Database

`FinalCheckDbContext`

或创建它的 Factory 不得继续硬编码数据库位置。

数据库位置必须由：

`IDataRootProvider`

提供。

---

## 7.2 Working Copy

Format Restore Working Copy 必须跟随 DataRoot。

但：

用户原始合同文件不移动。

---

## 7.3 Cache 与 Temp

区分：

### Durable Data

必须跟随 DataRoot，例如：

- Database
- Snapshot
- Comparison
- Restore history
- Working Copy
- Backup

### Disposable Data

可以单独由 cache/temp provider 管理。

如果用户更换 DataRoot：

缓存可以选择重建，不必全部迁移。

将边界写入文档。

---

## 7.4 Log

日志是否跟随 DataRoot，应按照现有 architecture 决策实现。

如果日志用于恢复诊断：

建议保留可定位策略。

不要因为 DataRoot 不可用导致完全没有启动日志。

---

## Phase 2 验收

至少：

- 默认路径下 App 正常；
- 自定义 DataRoot 下数据库正常；
- Snapshot 正常；
- Comparison 正常；
- Restore Working Copy 正常；
- 测试不依赖机器固定用户名；
- 无安装目录数据写入。

完成后：

`Phase 2 — DONE`

建议 commit：

`refactor: route application data through data root`

---

# 8. Phase 3 — DataRoot Validation

建立正式：

`IDataRootValidator`

或同等能力。

---

## 8.1 Path 检查

至少验证：

- absolute path
- directory existence / creatability
- read permission
- write permission
- free disk space
- path is not install directory
- path is not obvious temporary directory

---

## 8.2 Fixed local disk

v0.1.0 第一阶段正式支持：

> 本机固定磁盘。

Windows 下应识别 drive type。

---

## 8.3 不支持 / 风险路径

首阶段至少检测：

- Network drive
- UNC path
- Removable drive

这些应拒绝作为主 DataRoot。

---

## 8.4 同步目录

尽量识别明显同步目录，例如：

- OneDrive

如果可靠判断：

拒绝或给出 Unsupported / HighRisk。

如果不能可靠识别全部同步服务：

不要声称完全识别。

架构和 UI 后续应明确：

> 仅保证普通本机固定磁盘路径。

---

## 8.5 空间

迁移前需要计算：

- 当前 DataRoot 大小；
- 迁移临时副本所需空间；
- 安全余量。

不得只判断：

`free > used`

应考虑 temporary copy + database backup + 操作余量。

---

## Phase 3 验收

测试至少覆盖：

- C/D 普通本地路径；
- relative path；
- readonly；
- 不可写；
- 不存在但可创建；
- network；
- removable；
- install root；
- 空间不足模拟。

完成后：

`Phase 3 — DONE`

建议 commit：

`feat: validate application data locations`

---

# 9. Phase 4 — DataRoot Migration Service

建立正式：

`IDataRootMigrationService`

或等价服务。

---

## 9.1 迁移流程

必须遵循：

```text
Validate target
↓
Calculate space
↓
Acquire storage migration lock
↓
Block new writes
↓
Create target staging directory
↓
Backup/copy durable data
↓
Validate copied data
↓
Open target database
↓
Database integrity check
↓
Validate managed files
↓
Switch runtime services
↓
Atomic bootstrap update
↓
Release lock
↓
Refresh application state
```

旧数据不能在这个过程自动删除。

---

# 10. SQLite 一致性迁移

不得在数据库打开且 WAL 可能存在的情况下：

> 直接复制单个 finalcheck.db。

优先使用 SQLite 官方 Backup API / EF 对应可控方案。

必须考虑：

- main db
- WAL
- SHM
- active connection

目标是得到一致的新数据库。

---

# 11. Migration Lock

迁移期间必须阻止：

- 新项目写入；
- Snapshot 写入；
- Comparison 持久化；
- Format Restore operation；
- 其他 durable-data 写入。

读取是否允许由实现决定。

但不能发生：

> 复制到一半旧库继续增加数据，而目标漏掉。

---

# 12. Staging

不得直接往目标正式目录边拷边启用。

使用类似：

`<target>\.finalcheck-migration-<id>`

或同等 staging。

全部验证后再完成切换。

---

# 13. 数据校验

迁移完成前至少验证：

### Database

- 可打开；
- integrity check；
- schema version；
- 核心表数量；
- 关键历史记录数量。

### Managed Files

至少：

- file count
- size
- SHA-256（关键 durable 文件）

不要求给大量 cache 逐文件 SHA。

---

# 14. Bootstrap 切换

只有：

> 目标数据已经全部验证通过

才能更新 bootstrap。

更新时：

- 原子写；
- Previous / LastKnownGood 保留。

---

# 15. Runtime 切换

目标：

> 更改 DataRoot 后无需重启应用。

如果当前架构无法安全热切换 DbContext / repositories：

不要用脆弱 Hack。

允许本阶段形成安全的 storage service rebind / scope restart 机制。

如果技术上确实需要重新加载 Application Data Session：

也应在应用内部完成，不要求整个进程退出重启。

---

# 16. 失败恢复

任何阶段失败：

- bootstrap 继续指向旧 DataRoot；
- 旧数据库继续可用；
- 旧文件不得删除；
- runtime 回到旧 storage session；
- staging 标记为未完成；
- 返回明确失败阶段。

不得出现：

> 迁移失败后两个目录都被修改到无法使用。

---

# 17. Crash Recovery

如果程序在迁移期间崩溃：

下次启动读取 bootstrap 时：

必须能够判断：

- 正常 DataRoot；
- 未完成 staging；
- 已准备但尚未切换；
- bootstrap 临时文件。

默认优先使用：

> LastKnownGoodDataRoot

不得自动删除 staging。

可以生成 recovery diagnostic。

---

# 18. 迁移 Operation Record

保存必要记录：

- MigrationId
- Source
- Target
- StartedAt
- CompletedAt
- Status
- FailureStage
- Size
- Validation result

如果 operation record 存数据库会产生切换依赖问题：

可以合理设计 bootstrap-side / journal-side 记录。

形成文档说明。

---

## Phase 4 验收

至少覆盖：

- 成功迁移；
- 数据库 backup；
- files 复制；
- target reopen；
- bootstrap switch；
- failed copy；
- integrity failure；
- space failure；
- cancellation；
- crash-like interrupted migration；
- old root preserved。

完成后：

`Phase 4 — DONE`

建议 commit：

`feat: add safe data root migration`

---

# 19. Phase 5 — Runtime Integration / Developer Validation

把基础设施接入 Desktop / App 生命周期。

本阶段不做完整正式 Storage Settings UI。

---

## 19.1 Startup

启动顺序至少：

```text
Read bootstrap
↓
Resolve DataRoot
↓
Validate minimally
↓
Recover if necessary
↓
Create data services
↓
Open application
```

不能：

> 先创建固定路径 DbContext，再读取 bootstrap。

---

## 19.2 Developer 验证入口

如当前正式 UI 尚未适合增加设置页：

允许创建 Debug-only Storage Developer 页面或命令。

支持：

- 查看 DataRoot；
- 查看数据库路径；
- 查看各目录；
- 验证候选路径；
- 触发测试迁移；
- 查看 migration result。

Release 用户 UI 暂不暴露。

---

## 19.3 Storage usage service

为后续 UI 建立：

`IStorageUsageService`

至少可以统计：

- Database
- Snapshot
- Comparison
- Working Copy
- Backup
- Cache
- Log/Temp
- Total

不要求实时监控。

按需计算即可。

---

## 19.4 测试旧用户升级

必须重点测试：

> 当前已经使用 Final Check 的开发机器升级到加入 bootstrap 的新版本。

确保：

- 原数据库仍存在；
- 项目历史仍能读取；
- Snapshot/Comparison/Restore 历史仍然存在；
- 不生成空白新数据库覆盖体验。

---

## Phase 5 验收

至少：

- fresh startup；
- existing-user startup；
- custom DataRoot startup；
- migration without app restart；
- storage usage；
- Desktop 正常启动；
- Test Build 不回归。

完成后：

`Phase 5 — DONE`

建议 commit：

`feat: integrate configurable application data root`

---

# 20. 安全约束

绝对禁止：

- 自动删除旧 DataRoot；
- 迁移时移动用户原始 DOCX；
- 数据库存安装目录；
- 网络路径直接当正常 SQLite 主库；
- 失败后 bootstrap 指向未验证目标；
- 为通过测试删除 migration failure 分支。

---

# 21. DataRoot 与原始 DOCX

再次明确：

DataRoot 管理的是：

> Final Check 自己产生和维护的数据。

不管理用户原始合同文件。

Original DOCX：

- path
- hash
- metadata

继续由项目数据记录。

不得因为用户改变 DataRoot：

> 把全部用户合同复制到 D:\FinalCheckData。

---

# 22. DataRoot 与导出

用户导出的：

- restored DOCX
- Excel
- Markdown
- TXT

仍按照 PRD 的 Export Location 规则。

不得默认改为 DataRoot。

---

# 23. Backup

本轮不实现完整产品 Backup UI。

但 DataRoot 架构必须确保以后：

> Export Software Backup

能够明确知道哪些 durable data 需要纳入。

---

# 24. 测试

新增单元 / 集成测试。

测试中：

必须使用隔离 Temp Directory。

禁止操作开发者真实：

`%LocalAppData%\FinalCheck`

测试完成清理自身目录。

---

# 25. 性能

DataRoot Provider 本身不能成为高开销路径。

不要每获取一个路径：

- 重读 bootstrap；
- 扫磁盘；
- 计算空间。

Bootstrap 可合理缓存。

Migration / Usage 属于显式重任务。

---

# 26. 跨平台

Storage Core abstraction 保持跨平台。

Windows-specific：

- Drive type
- LocalAppData
- install-path comparison

放入 Infrastructure / platform implementation。

macOS core CI 继续通过。

未来 macOS 可以提供：

`~/Library/Application Support/FinalCheck`

等对应实现，但本阶段不需要正式启用。

---

# 27. 文档

完成后更新：

- `docs/architecture/storage-and-paths.md`
- `docs/development/README.md`

如果实现细节对 ADR-0006 有实质补充：

更新 ADR。

如产生新的重大决定：

可新增 ADR。

---

# 28. Task 状态

- Phase 1 — DataRoot / Bootstrap：DONE
- Phase 2 — Path Adoption：DONE
- Phase 3 — Validation：TODO
- Phase 4 — Migration：TODO
- Phase 5 — Runtime Integration：TODO

Codex 持续维护实际状态。

## 实际完成与验证记录

### Phase 1 — DataRoot / Bootstrap

- Core 新增不可变 `DataRootDescriptor` / `IDataRootProvider` / `DataRootPaths`，统一数据库、Working Copy、Backup、Cache、Logs、Temp；Snapshot payload 当前位于数据库，不虚构独立文件夹。
- Infrastructure 新增平台配置位置、原子 bootstrap store、root identity 和启动 resolver；SQLite 数据库识别在 Data 内以只读方式检查完整性、外键、已知 migration 和表结构，未调用 EF 初始化或创建空库。
- 新用户注册默认 Data；旧用户自动识别并指向原位置，保留原数据库字节与历史。损坏配置、缺失既有数据库、多个候选库、未知 migration / root identity 均阻断，不猜测覆盖。
- LastKnownGood 指向当前已提交且已验证的同一 RootId / generation；能修复损坏的位置字段，但旧迁移源或 previous locator 不是自动回退依据，以免提交后产生旧库分叉。损坏 JSON 没有可验证证据时明确停止。
- 阶段测试：20 个新增 StorageBootstrap tests；Release 全量测试 145/145（原有 125 无回归），全部隔离在 GUID 临时目录，无正常用户数据库/原始 DOCX 访问。
- 尚未接入 Desktop，也未实现路径政策、写入维护屏障或迁移；分别留在后续 Phase。

### Phase 2 — Path Adoption

- Desktop 在 Velopack lifecycle 后先读取 / 验证 bootstrap，再注册统一 DataRoot 和数据 scope；`IAppDataPathProvider` 成为兼容 adapter，不独立选择根目录。
- 新增 `DataRootDbContextFactory`，connection string builder 支持中文、空格、分号。Snapshot / Comparison / restore journal 沿同一库持久化，Working Copy 与现有 per-version backup 跟随根目录，不改变发布 / Undo 算法，也不移动原始 DOCX。
- EF design-time 使用独立 tool-only root，不读取正常用户 locator。Debug override 同时隔离 DataRoot 与 sibling bootstrap；启动失败的最小诊断保留在配置位置，不写异常 message / 合同内容。
- Durable 包括 SQLite / 三类 payload / journal、WorkingCopies / 版本备份、Backups。Cache / Temp 可重建，但本任务继续完整复制；业务 Logs 在 DataRoot，bootstrap / startup-control 文件排除于业务迁移。
- 新增 4 个路径接入测试，Release 全量测试 149/149；Release build 零警告、零错误。默认 / 自定义 root 验证真实生成 DOCX 的恢复、持久化、重新打开与原文不变。
- Debug App 冒烟使用 GUID 隔离目录 `C:\Users\KB\AppData\Local\Temp\FinalCheck.Storage.Smoke-5a9275d0efd3444688b2c06914a98a7f`：bootstrap / SQLite 创建，window handle 非零，Responding=true，正常关闭；未启动正常用户数据库。
- 路径接入不等于已完成迁移 / 热切换，后续 Phase 将加入共同维护屏障和 data session rebind。

---

# 29. 最终验证

至少：

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

确认：

- Existing 125 tests 不回归；
- 新增 Storage tests 通过；
- Windows CI PASS；
- macOS core CI PASS；
- App startup PASS。

---

# 30. Test Build

全部完成后允许触发一次：

> Build Windows test installer

验证加入 DataRoot 基础设施后：

- Setup build；
- Portable build；
- App startup。

不得创建 Tag / Release。

---

# 31. 最终报告

## Storage Foundation v0

### DataRoot

- Default：
- Custom：
- Provider：
- Durable paths：
- Disposable paths：

### Bootstrap

- Location：
- Schema：
- Atomic write：
- Corruption recovery：
- Existing-user adoption：

### Validation

- Local fixed disk：
- Permission：
- Free space：
- Network：
- Removable：
- Sync folder：

### Migration

- SQLite strategy：
- WAL safety：
- Staging：
- Validation：
- Runtime switch：
- Failure rollback：
- Crash recovery：
- Old data preservation：

### Storage Usage

- Database：
- Snapshot：
- Comparison：
- Working Copy：
- Backup：
- Cache：
- Total：

### Compatibility

- Existing user：
- Snapshot history：
- Comparison history：
- Restore history：

### Tests

- Total：
- Passed：
- Failed：

### Platform

- Windows：
- macOS CI：

### Packaging

- Test build：

### Documentation

- storage-and-paths：
- ADR：

### Git

- Phase commits：
- Push：
- Working tree：

### Known Limitations

明确列出尚未实现的：

- 正式 Storage Settings UI
- Installer Wizard
- 网络 / 可移动 / 云同步目录
- 自动旧数据清理
- 其他限制

最终判断：

> Storage Foundation v0 是否已经具备进入正式 Comparison UI 开发阶段的稳定基础。
