# Comparison Engine v0

## 边界与输入输出

Comparison Engine 只接受不可变的 `DocumentSnapshot`，不直接读取 DOCX 文件路径，也不持有 Open XML SDK 对象。稳定的比较结果模型位于 `FinalCheck.Core.Comparisons`，算法接口和实现位于 `FinalCheck.Comparison`，从而保持 UI、文档解析与持久化实现彼此解耦。

当前正式输出为 `ComparisonResult` schema version 1，包含：

- 基准与当前 Snapshot 标识及比较元数据；
- 可独立于 ChangeItem 使用的节点映射；
- 结构化 ChangeItem、差异 span 与格式属性差异；
- 规则归并结果、诊断和统计信息；
- 原生修订、批注和匹配证据的关联字段。

Snapshot 没有内容哈希时，比较引擎根据 schema 与节点内容生成确定性 SHA-256 标识，不使用运行时随机 ID。

## Phase 1：段落匹配

段落匹配按以下阶段执行：

1. 相同结构位置且规范文本完全相同；
2. 文档内唯一的规范文本完全匹配；
3. 重复完全相同文本按稳定文档顺序匹配并记录歧义诊断；
4. 对剩余段落计算多信号候选分数；
5. 低于阈值或最佳候选过于接近时保持 Unmatched。

规范化使用 Unicode Form KC，统一换行和常见空白表示，折叠连续的无意义空白，但保留有意义的单个空格和段内换行。匹配始终保留并返回原始 Snapshot 文本，Word Run 如何切分不会影响段落文本判定。

多信号评分包含文本相似度、相对位置、样式、编号或标题键以及前后文一致性。候选生成先使用有限位置窗口，再使用 Token 倒排索引和标题键补充，单段最多计算固定数量的候选，避免对全部段落做笛卡尔积相似度比较。

匹配结果同时提供离散 confidence、数值分数和逐项 evidence。重复数量不一致、候选分数接近或最佳结果低于阈值时输出诊断；不确定结果不会伪装成高可信映射。

## 确定性

- 完全相同输入应产生相同的映射顺序、分数、证据和诊断顺序。
- 所有候选 tie-break 均使用位置和稳定文档顺序。
- 取消请求在各匹配阶段和候选计算循环内被检查。

## Phase 2：局部文字 Diff

成功映射且实际文本不同的段落使用 `ITextDiffService` 独立比较，不回退到全文级 Diff。`ITextTokenizer` 将中文字符、英文单词、数字、连续空白和标点分别建模，并保留每个 Token 在原始字符串中的 UTF-16 offset 与 length。

v0 使用最长公共子序列生成稳定的 Token 编辑序列，再把相邻插入与删除合并成 Replace span。每个 `DifferenceSpan` 同时保存基准与当前文本的 start、length 和原文，因此 UI 可以只突出真正变化的字、词、数字或标点。Tokenization 与 Diff 都不依赖 Word Run 边界，第三方类型也不会进入 Core 模型。

## Phase 3：段落结构变化

匹配完成后，移动检测按基准顺序排列映射，并以当前位置的最长递增子序列作为未改变相对顺序的骨架。骨架外的唯一或高相似映射形成 `ParagraphMove`；若文本同时变化，则形成携带局部 DifferenceSpan 的 `ParagraphMoveAndModify`。该算法为 O(n log n)，前部新增或中间删除导致的整体 index 偏移不会被误判为移动。

重复文本通过稳定文档顺序形成的中等可信 Contextual 映射不会仅凭相同文本认定移动；相关歧义继续保留在 diagnostics。真正未匹配的基准/当前段落分别形成 `ParagraphDelete` / `ParagraphInsert`。

## Phase 4：有效格式与表格

段落格式比较只读取 Snapshot 已计算的 `EffectiveFormatting`。字符级比较覆盖 Ascii、HighAnsi、EastAsia、ComplexScript 四字体槽，以及字号、颜色、粗体、斜体、下划线、删除线和高亮；段落级覆盖对齐、左右缩进、首行/悬挂缩进、段前段后与行距。相同 scope 的多个属性合并为一个 `ComparisonFormatDifference` 和一个顶级 ChangeItem。

字符格式投影按有效格式合并相邻 Run，因此仅改变 Word Run 切分不会形成格式差异。文字和格式同时变化时分别保留可精确高亮的文字 ChangeItem 与可展开的格式 ChangeItem。

表格 v0 按稳定的 table index、row index、column index 建立 Table/Cell mapping，比较 Snapshot 提供的 table/cell 格式，包括宽度、对齐、底纹、边框、GridSpan 与 VerticalMerge。单元格的文字和格式变化合并为一个 `TableCellChange`。行列结构无法一一对应时输出 `TableStructureChanged`，同时继续比较仍可可靠定位的交集单元格。当前 Snapshot 的 table/cell 仅提供 direct formatting，因此该范围使用其已解析值；不在 Comparison 层重新解析 Open XML。

## 后续 Phase

Phase 5 将在同一结果 schema 上增加修订/批注整合、规则归并和持久化。当前阶段不实现格式恢复、正式业务 UI 或 AI 语义分析。
