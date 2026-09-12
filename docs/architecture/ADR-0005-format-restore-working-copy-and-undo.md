# ADR-0005 — Format Restore Working Copy / Journal / Undo

- Status: Accepted
- Date: 2026-09-12
- Scope: Format Restore Engine v0

## Context

只恢复格式、保留当前文字/修订/批注，原始 DOCX 永不修改；每个 ContractVersion 一个当前 restored working file，且跨 SQLite 与文件系统失败时不能丢失旧 working file。当前没有项目管理实体和正式业务 UI，不提前扩展产品范围。

## Decision

1. Core 使用调用方稳定 ContractVersion Guid；Infrastructure 管理独立 AppData 下固定 working path。恢复规划复用 ComparisonNodeMapping，不另建匹配算法。
2. Documents 只修改支持的属性，优先 style/inheritance 与最小 direct override；不复制 baseline 正文/表格/样式库。未知属性、OPC parts、格式修订子树保护；私有输出正式重解析验证后才可发布。
3. Candidate → parse/hash validation → durable Prepared operation → recheck current SHA → atomic replace/safe move → SQLite metadata/operation completion。保存旧文件 backup。原子文件替换与数据库不能组成同一事务，因此使用 journal 与 before/after hashes 恢复，不声称跨资源绝对原子性。
4. metadata SHA 并发 token 与版本 operation.lock 防止本应用重复操作；symlink/reparse-point 拒绝。外部编辑器不是合作锁参与者，要求关闭文件编辑，hash 不符明确拒绝。
5. Undo 保存 before/after properties XML 及 affected node IDs，仅最近一次成功格式操作可撤销；after SHA 与属性状态必须匹配。Undo 走相同验证/发布管线，不恢复文字、不接受修订。
6. operation schema 1 payload、working metadata 使用数据库 schema 3 两个新增表。迁移保留既有 DocumentSnapshot/ComparisonResult 历史。PreserveExternalChanges 追加新 Snapshot/Comparison；Regenerate 显式读取原文件重建并保留旧 working backup。未知 schema/不一致 metadata 拒绝。

## Consequences

崩溃恢复按 after/before/unknown SHA 分别完成/标失败/NeedsReview；不猜测覆盖。如果数据库提交状态无法确认，保留 journal 与文件待调查。当前内部 backup 保守保留，未来需要非破坏式保留/清理策略；不是无限 Undo Stack。内存私有 package + snapshots + protected XML 有线性分配开销，记录 small/medium 基线，暂不引入大型商业 SDK。正式 UI、导出、合同项目实体、格式视觉渲染、结构恢复与 updater UI 均不在本 ADR 实现范围。
