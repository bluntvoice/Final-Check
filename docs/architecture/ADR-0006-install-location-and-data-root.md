# ADR-0006：可选安装目录与独立、可迁移 DataRoot

- 状态：Accepted；Storage Foundation Phase 1–5 已实现，最终平台/打包验收以 task 为准；Installer Spike 尚未验收。
- 日期：2026-09-12
- 适用版本：v0.1.0 development
- 部分取代：ADR-0004 中“固定默认安装路径即可满足产品”的假设；保留其 Velopack、版本、channel、feed 和发布安全决策。ADR-0005 的 Working Copy 安全管线保持不变，路径归属未来改为 DataRoot。

## 背景

用户必须能看到并选择 Windows 安装目录，也能独立更改业务数据位置。当前默认 Velopack Setup 没有目录选择向导，`IAppDataPathProvider` 固定旧 AppData 路径；已有 SQLite restore payload 含受校验的绝对文件路径。直接切换目录会造成空库假象、旧路径引用和双写风险。

## 决策

1. Application install location and application data location are independent concepts.
2. User data must never be stored inside the replaceable application installation directory.
3. 首装显示安装目录与 Browse，支持用户可写的本地固定磁盘路径；维护/更新沿用实际实例根目录，卸载以实际位置为准。Test / Prerelease / Stable 同一机制，固定 `FinalCheck.App`，不提供 side-by-side。
4. 推荐先 Spike 轻量 Final Check Wizard 包装未修改的官方 Setup，保留 Velopack updater / Registry / shortcut / uninstall 所有权。MSI 和指定目录属性不等于已具备浏览 UI；未经链路验证不替换现有已通过测试的 packaging workflow。
5. 正式新用户默认 DataRoot 为 `%LocalAppData%\FinalCheck\Data`，SQLite 和所有业务运行数据统一归属 DataRoot。原始 DOCX 仅保留原路径；导出路径按 PRD 第 48 章，不随 DataRoot 改变。
6. 小型 bootstrap 留在系统默认配置位置 `%LocalAppData%\FinalCheck\bootstrap.json`，先于 SQLite 定位 DataRoot；存储版本与数据库/Snapshot/Comparison/restore schema 独立。损坏/不可达/未知版本不能静默创建空库。
7. 迁移使用全局维护屏障、完整一致性复制、验证、预备新数据层和原子 bootstrap 切换。旧数据默认保留；失败不剪切、不清空、不自动覆盖或删除。以 durable operation journal 明确提交边界和崩溃恢复，不声称多个文件资源共同绝对原子。
8. 新的托管文件存储引用优先 DataRoot-relative path + root identity，并提供现有绝对路径 payload 的显式、带版本兼容迁移；仅重定位经验证的托管引用，不能改写用户原始/导出路径或文档内容身份。具体存储版本升级需后续测试，不在本轮提高 schema。
9. v0.1.0 首阶段只支持可写的 Windows 本机固定磁盘绝对目录。NAS/SMB/映射网络盘、可移动盘、已识别同步目录及链接路径不作为 SQLite 主数据根目录支持；不以警告点击代替验证。

## 影响与实施边界

- 现有旧路径继续工作；没有 bootstrap 的旧数据先识别，不能直接改默认根目录。启动兼容、managed path 迁移、上下文/连接池切换、跨进程写入协调和即时 UI 刷新须一起验收。
- 本轮只增加需求/架构/接口设计，不接入 IDataRootProvider 或生成 bootstrap，不启动/迁移正常用户数据库，不新增依赖、修改业务代码、改 workflow、创建 Tag / Release。
- 后续 Storage Settings 阶段按 [storage-and-paths.md](storage-and-paths.md) 实现；安装器独立按 [installer-architecture.md](installer-architecture.md) 的 Spike 与三类包门禁实现。Accepted 不代表上述功能已完成。

## Storage Foundation 实施补充（2026-09-13）

- Phase 1–4 已实现 bootstrap、旧库只读识别、路径政策、scope-lifetime 共同维护屏障、SQLite Backup API / WAL 一致性验证、完整 staging、强类型托管路径重定位及 durable journal。此前“本轮只做文档”是原架构任务边界，不限制后续已授权的 Storage Foundation 实施。
- 当前领域 payload schema 与布局不升级；仅在目标副本转换明确托管路径，旧库及 audit DB backup 保留原 payload。Snapshot schema 1 的历史字节亦保持不变，OriginalPath 与文档 SHA / identity / Undo 语义不变。
- LastKnownGood 为当前已提交 RootId / generation 的可信定位，previous 或迁移源不是新库已有写入后的自动回退来源。提交无法确认则阻断数据写入；不猜测制造旧库分叉。
- Scope 全生命周期持有跨进程协作锁，排空后通过内部 session rebind 热切换；不强制中断现有事务。正式设置 UI、安装 Wizard、staging 自动续传与旧数据自动清理不在本阶段。
- Phase 5 已接入真实 Desktop 启动恢复、固定盘政策和按需物理/逻辑占用服务；成功切换后发 generation 通知，刷新失败不能回滚 durable locator。Debug 隔离命令不编译进 Release，没有正式用户路径 override 后门。未提交中断 receipt 明确结束失败尝试且保留文件，以免后续重试成功后过时 journal 阻断启动。
