# Format Restore Engine v0

## Pipeline 与分层

Analyze Snapshot + existing Comparison → deterministic Restore Plan → review/validate → execute on a separate copy → official Document Engine reparse → validate → publish working copy and history.

Core 保存 schema 1 纯领域 Restore Plan/Item、Scope、Policy、Diagnostic 与稳定接口；Comparison 的 planner 只消费 Snapshot 和正式 `ComparisonNodeMapping`，不重新匹配段落。Documents 负责 Open XML 属性级写回，Infrastructure 负责文件生命周期，Data 保存 payload/history。核心层不依赖 Word、COM、Registry 或 Velopack。

## Plan / Mapping / Confidence

Plan 保存 source SHA-256、两侧 Snapshot identity、比较结果内容哈希、policy 和生成时间。PlanId / ItemId 按输入内容 SHA-256 产生；默认 CreatedAt 来自 current metadata，缺失时使用 Unix epoch，调用方可显式提供时间。相同输入具有确定性的顺序、ID 与 fallback。

自动恢复只接受 Exact/High 且 score ≥ 0.8 的映射；Medium/Low、非法分数、重复映射和错误 Snapshot identity 不会静默执行。映射和 RestoreItem 解耦，同一映射可产生字符/段落等不同 scope 的项目，多属性合并为一个节点项目。分析永不打开可写 DOCX。

## 当前进度

Phase 1–4 已建立计划、字符、段落及 Table/Cell direct 属性恢复。真实文件生命周期、Undo 与持久化按 Phase 5 加入。

## Character restore / 新增文字

复用 Comparison 的 UTF-16 text diff。先按显示位置投影到 baseline Run；Replace 使用被替换区间的目标格式，Insert 优先使用对应段落统一目标格式，再使用插入点左右映射文本一致的目标格式。左右格式冲突、一个 current Run 跨多个目标格式或删除修订无可靠目标时跳过并诊断，不分割 Run、不猜测样式。不要求两侧 Run 边界一致。

只修改 current Run 的支持格式属性，不修改文字或修订容器；不修改全局 Style 定义和 defaults。写回先移除发生差异的 direct 属性，重新解析其 inherited/effective 状态；继承已达到 target 时不写 direct，只对仍不匹配的属性写最小 override。字体只写发生差异的槽，保留其他槽及未知属性；无法表示的缺失目标值拒绝输出，不把全部 effective 格式铺成 direct。

## Reparse / 保留验证

Renderer 在私有 MemoryStream 中编辑，正式 Document Engine 重新解析；验证目标有效格式、所有 Run 的 raw/display/content、revision 完整模型、comment/anchor、节点身份和表格数量。额外按 OPC part 检查：其他 part 原样保留，正文 XML 只允许支持的属性变化；已有格式修订子树完全保留，不接受修订、不创建 Change 节点。取消不返回未验证结果。所有真实文件生命周期在 Phase 5 独立实现。

## Paragraph restore / Style / 新增段落

只修改 pPr 中对齐、缩进、首行/悬挂、段前后及行距/rule 支持属性。优先去除 override 复用继承；可用的 target Style reference 仅在其未知属性/字符属性继承链兼容、且切换不改变当前 Run 有效字符格式时采用。缺失/不兼容样式保留当前 reference，支持属性使用最小 direct override 并报告 StyleReferencePreserved；不导入或覆盖整个样式库。

新增段落 fallback 只接受同一容器、同编号层级和 style profile 的前后两个 Exact/High 映射，且两侧 baseline 目标格式/样式一致；引用原始邻近 mapping 作为额外恢复证据，并报告 AddedParagraphFallbackUsed。没有双侧一致证据的首尾新增段落/层级冲突保留未处理诊断，不随机选 global/template 自定义样式。未来上层可提供人工样式选择，v0 不建立不存在的样式库。

## Table / Cell / 能力边界

Reliable：Snapshot 已解析的 direct table 宽度/对齐/底纹/基础六边框，cell 宽度/垂直对齐/底纹/边框，以及相同结构下的 row height/rule。只在已有 Table/Cell mapping 上工作。可信 cell 下数量一致的直接 child paragraphs/Run 可投影恢复字符/段落，引用 parent Cell mapping，不另建段落匹配算法；row identity 同样从可信 cell 及不变结构推导。

Partial：结构变化时仅恢复同位置 mapping 且两侧均唯一、文字完全相同的 cell。不得凭原 Comparison 的 Structural/Exact 标签就假装结构编辑后位置仍可信；其他 cell 需要 Review。数量变化的多个 tables 保守拒绝。未知属性和其他 parts 保留。

Unsupported：完整 table effective/conditional style、嵌套 table 展开、TableGrid 列宽继承和结构恢复。保留 current table StyleId，显式 UnsupportedTableStyle；GridSpan/VerticalMerge 始终用 current 值，不恢复 merge/split，不删除新增行列。表格 XML 不整体替换。
