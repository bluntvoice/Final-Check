# ADR-0002：DocumentSnapshot schema 与持久化

- 状态：Accepted
- 日期：2026-09-10
- 适用版本：v0.1.0

## 背景

Document Engine 需要把短生命周期的 Open XML DOM 转换为可长期保存、可跨平台读取且可供 Comparison Engine 消费的结构。Snapshot 会随解析能力扩展，历史数据又不能因模型升级被静默覆盖或误读。

## 决策

1. `DocumentSnapshot.CurrentSchemaVersion` 是 payload 的显式契约；Document Engine v0 使用 schema 2。
2. Snapshot 使用纯领域记录，不持有 Open XML SDK 对象。
3. 节点身份使用确定性结构路径，并保存父关系、类型、来源 Part 与源位置。
4. serializer 只写当前 schema；读取旧 schema 必须经过显式迁移，未来 schema 必须拒绝。
5. SQLite 以单个 Snapshot payload 保存结构，并单独保存 schema version；不为每个 Run 建立 EF Entity。
6. v0 正式 payload 使用 UTF-8 JSON。GZip/Brotli 仅记录 Spike 数据，暂不写入持久化格式。

## 理由

单一 payload 保持领域模型与数据库结构解耦，减少复杂文档产生的大量关系行。显式版本和迁移避免旧历史被当前模型误解释。确定性节点身份便于测试和后续映射，又不虚假承诺编辑后跨版本身份永远不变。保留原始 JSON 有利于 v0 调试和人工审查；压缩收益需结合真实合同、迁移与恢复成本后再决定。

## 影响

- 每次 schema 变化都要新增迁移测试和文档说明。
- 未能从旧版本恢复的字段必须产生 Partial/Diagnostic，不得伪装完整。
- payload 压缩如正式采用，需要新的存储格式标识和向后兼容路径。
- Comparison Engine 只依赖 Snapshot，不直接依赖 DOCX 文件或 Open XML Package。
- 数据库物理位置、DataRoot、bootstrap 与一致性目录迁移见 [storage-and-paths.md](storage-and-paths.md) / [ADR-0006](ADR-0006-install-location-and-data-root.md)。改变数据目录不改变 Snapshot 历史事实或 schema；payload 当前仍在 SQLite 内，统计不重复计算磁盘占用。
