# Format Restore Engine v0

## Pipeline 与分层

Analyze Snapshot + existing Comparison → deterministic Restore Plan → review/validate → execute on a separate copy → official Document Engine reparse → validate → publish working copy and history.

Core 保存 schema 1 纯领域 Restore Plan/Item、Scope、Policy、Diagnostic 与稳定接口；Comparison 的 planner 只消费 Snapshot 和正式 `ComparisonNodeMapping`，不重新匹配段落。Documents 负责 Open XML 属性级写回，Infrastructure 负责文件生命周期，Data 保存 payload/history。核心层不依赖 Word、COM、Registry 或 Velopack。

## Plan / Mapping / Confidence

Plan 保存 source SHA-256、两侧 Snapshot identity、比较结果内容哈希、policy 和生成时间。PlanId / ItemId 按输入内容 SHA-256 产生；默认 CreatedAt 来自 current metadata，缺失时使用 Unix epoch，调用方可显式提供时间。相同输入具有确定性的顺序、ID 与 fallback。

自动恢复只接受 Exact/High 且 score ≥ 0.8 的映射；Medium/Low、非法分数、重复映射和错误 Snapshot identity 不会静默执行。映射和 RestoreItem 解耦，同一映射可产生字符/段落等不同 scope 的项目，多属性合并为一个节点项目。分析永不打开可写 DOCX。

## 当前进度

Phase 1 建立计划模型和纯 Snapshot 生成，字符首先支持映射段落统一目标格式；混合 Run 需要后续安全位置投影。未匹配节点保留诊断。字符写回、段落、表格和文件持久化将按任务 Phase 顺序加入。
