# Format Restore Engine v0

## Pipeline 与分层

Analyze Snapshot + existing Comparison → deterministic Restore Plan → review/validate → execute on a separate copy → official Document Engine reparse → validate → publish working copy and history.

Core 保存 schema 1 纯领域 Restore Plan/Item、Scope、Policy、Diagnostic 与稳定接口；Comparison 的 planner 只消费 Snapshot 和正式 `ComparisonNodeMapping`，不重新匹配段落。Documents 负责 Open XML 属性级写回，Infrastructure 负责文件生命周期，Data 保存 payload/history。核心层不依赖 Word、COM、Registry 或 Velopack。

## Plan / Mapping / Confidence

Plan 保存 source SHA-256、两侧 Snapshot identity、比较结果内容哈希、policy 和生成时间。PlanId / ItemId 按输入内容 SHA-256 产生；默认 CreatedAt 来自 current metadata，缺失时使用 Unix epoch，调用方可显式提供时间。相同输入具有确定性的顺序、ID 与 fallback。

自动恢复只接受 Exact/High 且 score ≥ 0.8 的映射；Medium/Low、非法分数、重复映射和错误 Snapshot identity 不会静默执行。映射和 RestoreItem 解耦，同一映射可产生字符/段落等不同 scope 的项目，多属性合并为一个节点项目。分析永不打开可写 DOCX。

## Scope 与幂等

All、Category（Table 包含 Cell/Row）及 SelectedItems 支持未来预览/选择；未知 selected ID 拒绝。执行再次校验 Eligible 项目的原 Comparison confidence/score，不相信被修改的 eligibility。源 hash 不符拒绝旧 Plan；支持格式恢复后重新分析没有重复项目。不直接重用源 hash 已过期的 Plan。

## Character restore / 新增文字

复用 Comparison 的 UTF-16 text diff。先按显示位置投影到 baseline Run；Replace 使用被替换区间的目标格式，Insert 优先使用对应段落统一目标格式，再使用插入点左右映射文本一致的目标格式。左右格式冲突、一个 current Run 跨多个目标格式或删除修订无可靠目标时跳过并诊断，不分割 Run、不猜测样式。不要求两侧 Run 边界一致。

只修改 current Run 的支持格式属性，不修改文字或修订容器；不修改全局 Style 定义和 defaults。写回先移除发生差异的 direct 属性，重新解析其 inherited/effective 状态；继承已达到 target 时不写 direct，只对仍不匹配的属性写最小 override。字体只写发生差异的槽，保留其他槽及未知属性；无法表示的缺失目标值拒绝输出，不把全部 effective 格式铺成 direct。

## Reparse / 保留验证

Renderer 在私有 MemoryStream 中编辑，正式 Document Engine 重新解析；验证目标有效格式、所有 Run 的 raw/display/content、revision 完整模型、comment/anchor、节点身份和表格数量。额外按 OPC part 检查：其他 part 原样保留，正文 XML 只允许支持的属性变化；已有格式修订子树完全保留，不接受修订、不创建 Change 节点。保护 XML 按 expanded namespace names/属性/内容比较，不把 SDK 增加的冗余 xmlns 误判为正文改变；其他 parts 仍逐字节比较。

## Paragraph restore / Style / 新增段落

只修改 pPr 中对齐、缩进、首行/悬挂、段前后及行距/rule 支持属性。优先去除 override 复用继承；可用的 target Style reference 仅在其未知属性/字符属性继承链兼容、且切换不改变当前 Run 有效字符格式时采用。缺失/不兼容样式保留当前 reference，支持属性使用最小 direct override 并报告 StyleReferencePreserved；不导入或覆盖整个样式库。

新增段落 fallback 只接受同一容器、同编号层级和 style profile 的前后两个 Exact/High 映射，且两侧 baseline 目标格式/样式一致；引用原始邻近 mapping 作为额外恢复证据，并报告 AddedParagraphFallbackUsed。没有双侧一致证据的首尾新增段落/层级冲突保留未处理诊断，不随机选 global/template 自定义样式。未来上层可提供人工样式选择，v0 不建立不存在的样式库。

## Table / Cell / 能力边界

Reliable：Snapshot 已解析的 direct table 宽度/对齐/底纹/基础六边框，cell 宽度/垂直对齐/底纹/边框，以及相同结构下的 row height/rule。只在已有 Table/Cell mapping 上工作。可信 cell 下数量一致的直接 child paragraphs/Run 可投影恢复字符/段落，引用 parent Cell mapping，不另建段落匹配算法；row identity 同样从可信 cell 及不变结构推导。

Partial：结构变化时仅恢复同位置 mapping 且两侧均唯一、文字完全相同的 cell。不得凭原 Comparison 的 Structural/Exact 标签就假装结构编辑后位置仍可信；其他 cell 需要 Review。数量变化的多个 tables 保守拒绝。未知属性和其他 parts 保留。

Unsupported：完整 table effective/conditional style、嵌套 table 展开、TableGrid 列宽继承和结构恢复。保留 current table StyleId，显式 UnsupportedTableStyle；GridSpan/VerticalMerge 始终用 current 值，不恢复 merge/split，不删除新增行列。表格 XML 不整体替换。

Table/Cell 沿用 Comparison 的结构位置 mapping，不额外识别同尺寸表格内的语义行列重排；自动格式目标仍按已有 mapping，不删除/重排当前结构。正式 UI 应展示 mapping evidence，不能把结构位置映射宣称为新的语义表格匹配能力。

## Working Copy / Atomic write / Recovery

`IFormatRestoreWorkingCopyService` 接受调用方稳定的 ContractVersion Guid；v0 不提前建立项目管理实体。每个版本只维护 `AppData/WorkingCopies/<version-guid>/restored.docx`。原文件仅以路径/hash 标识，打开只读，不设置 readonly、不替换原文件。后续恢复使用当前 working file；原文件和其他版本文件不参与覆盖。

私有 Renderer 验证 → 唯一 candidate 文件（CreateNew、flush）→ 再正式 parse/hash 验证 → 保存 Prepared journal → 再查旧 working hash → File.Replace（含旧文件 backup）/首次 safe move → 数据库 transaction Completed + working metadata。发布临界区不响应取消，避免“已发布但被标记取消”；取消发生在临界区前则旧文件保持完整。跨进程 operation.lock 仅协调本应用，同样拒绝 symlink/reparse-point 路径。文件系统与 SQLite 不是共同事务，详细决策见 ADR-0005。

发布后数据库失败：确认是否已提交；已提交不 rollback，否则 hash 仍是本操作 after 时从 backup 回滚。数据库状态无法确定或检测到外部编辑则保留文件/backup/journal并返回 RecoveryNeedsReview，不猜测。恢复 Prepared 时：after hash 且重新 parse 与记录一致则完成 metadata；before hash 则标失败；其他 hash 进入人工调查。不会把未验证 candidate 自动升级为工作文件。仅清理本操作唯一临时 candidate，不清理用户文件。

备份是内部失败恢复材料，不是暴露的 restore-1/2/3 当前文档。v0 保守保留历史 backup；暂不实现自动历史清理和无限 Undo，后续需要可恢复的保留策略。普通 Word/WPS 不遵循本应用锁，最终 hash 检查缩小并发窗口，但不能代替用户关闭外部编辑器或提供任意文件系统上的跨进程 CAS 保证。

## Undo / External modification

仅最近一次 Completed Restore 可撤销。操作保存节点身份、属性 kind、before/after properties XML、before/after working SHA。Undo 先核对当前 after hash，再核对每个属性 after 状态，仅恢复原属性，不复制 Run/Paragraph/Table 正文；再次正式重解析/保留验证后走同一原子发布流程。Undo operation 标记父 operation Undone，连续 Undo 拒绝。

hash 不一致返回 WorkingCopyExternallyModified/UndoHashMismatch，不自动覆盖。显式 PreserveExternalChanges 重新 parse、Comparison、更新 working metadata，并追加新的 DocumentSnapshot 和 ComparisonResult 历史；文件字节不改。显式 Regenerate 从记录原路径读取当前原文件，生成相同稳定 working path，并保留被替换文件的内部 backup。两者都使之前的格式 Undo 失效，历史仍保留。本阶段无正式选择 Dialog/UI。

## Persistence / Diagnostics / Progress

Database schema 3 migration 只新增 RestoredWorkingCopies 与 FormatRestoreOperations。领域 operation schema 1 JSON payload 包含 Plan/Items/Scope、mutations、hash、时间、diagnostics、Undo link 与恢复 journal；working SHA 使用并发 token，metadata/operation 一致性与未知 schema 拒绝。历史 Snapshot/Comparison payload 不改、不删；外部编辑接受生成新历史。数据库文件仍在独立 AppData，不进入安装目录。

阶段进度涵盖 preparing/mappings/target/plan validation、working preparation、character/paragraph/table、saving/reparse/validation/completed；不伪造百分比。LowConfidenceMapping、UnmappedNode、AddedText/ParagraphFallbackUsed、UnsupportedTableStyle、MergeStructurePreserved、ExistingFormatRevisionPreserved、ExternalModificationDetected、UndoHashMismatch、WorkingCopyWriteFailed、RecoveryNeedsReview 等诊断供上层展示；失败不标 Completed。异常诊断不记录合同正文。

## 验证与已知限制

生成式非敏感 Fixtures 覆盖文字改变、新增、Run 边界、样式、表格结构、五类格式修订、文字修订/批注、Undo、外部编辑、迁移、取消、锁冲突、数据库发布失败回滚和 crash journal recovery。语义 golden assertions 验证内容/格式/XML，不依赖成功输出与输入 binary hash 相同。性能基线按 small/medium 追加到 development 文档。

v0 保守跳过：混合格式且 current 单 Run 无法安全投影、低可信/无双侧邻近共识的新段落、复杂或缺失样式系统、嵌套表格/完整 conditional table effective formatting、merge/split 结构恢复。未支持属性和未处理 package parts 保留。没有 Word/WPS 的视觉排版引擎或手工真实合同验收；正式 UI 应展示 Partial/NeedsReview 并让用户确认，不将本阶段包装为全能 Word 格式恢复。

实测边界：没有 style/条款/邻近上下文的单段 30→60 数字变化，在现有 Comparison 可得到 Medium / 0.770 映射。RestoreItem 标记 NeedsReview，执行返回 NoChanges + LowConfidenceMapping，不降低阈值；带一致条款线索的可信映射才自动恢复并保留 60。上层必须区分“无差异”与“因低可信跳过”，展示 diagnostics。
