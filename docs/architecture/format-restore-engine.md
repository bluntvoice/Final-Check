# Format Restore Engine v0

## Pipeline 与分层

Analyze Snapshot + existing Comparison → deterministic Restore Plan → review/validate → execute on a separate copy → official Document Engine reparse → validate → publish working copy and history.

Core 保存 schema 1 纯领域 Restore Plan/Item、Scope、Policy、Diagnostic 与稳定接口；Comparison 的 planner 只消费 Snapshot 和正式 `ComparisonNodeMapping`，不重新匹配段落。Documents 负责 Open XML 属性级写回，Infrastructure 负责文件生命周期，Data 保存 payload/history。核心层不依赖 Word、COM、Registry 或 Velopack。

## Plan / Mapping / Confidence

Plan 保存 source SHA-256、两侧 Snapshot identity、比较结果内容哈希、policy 和生成时间。PlanId / ItemId 按输入内容 SHA-256 产生；默认 CreatedAt 来自 current metadata，缺失时使用 Unix epoch，调用方可显式提供时间。相同输入具有确定性的顺序、ID 与 fallback。

自动恢复只接受 Exact/High 且 score ≥ 0.8 的映射；Medium/Low、非法分数、重复映射和错误 Snapshot identity 不会静默执行。映射和 RestoreItem 解耦，同一映射可产生字符/段落等不同 scope 的项目，多属性合并为一个节点项目。分析永不打开可写 DOCX。

## 当前进度

Phase 1 建立计划模型和纯 Snapshot 生成；Phase 2 已加入字符写回和安全位置投影。段落、表格和文件持久化按任务 Phase 顺序加入。

## Character restore / 新增文字

复用 Comparison 的 UTF-16 text diff。先按显示位置投影到 baseline Run；Replace 使用被替换区间的目标格式，Insert 优先使用对应段落统一目标格式，再使用插入点左右映射文本一致的目标格式。左右格式冲突、一个 current Run 跨多个目标格式或删除修订无可靠目标时跳过并诊断，不分割 Run、不猜测样式。不要求两侧 Run 边界一致。

只修改 current Run 的支持格式属性，不修改文字或修订容器；不修改全局 Style 定义和 defaults。写回先移除发生差异的 direct 属性，重新解析其 inherited/effective 状态；继承已达到 target 时不写 direct，只对仍不匹配的属性写最小 override。字体只写发生差异的槽，保留其他槽及未知属性；无法表示的缺失目标值拒绝输出，不把全部 effective 格式铺成 direct。

## Reparse / 保留验证

Renderer 在私有 MemoryStream 中编辑，正式 Document Engine 重新解析；验证目标有效格式、所有 Run 的 raw/display/content、revision 完整模型、comment/anchor、节点身份和表格数量。额外按 OPC part 检查：其他 part 原样保留，正文 XML 只允许支持的属性变化；已有格式修订子树完全保留，不接受修订、不创建 Change 节点。取消不返回未验证结果。所有真实文件生命周期在 Phase 5 独立实现。
