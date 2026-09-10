# Final Check — Comparison Engine v0

> Status: Planned
> Version: v0.1.0 development
> Scope: Comparison Engine
> Depends on: Document Engine v0
> Last updated: 2026-09-10

---

## 1. 任务目标

基于已经完成的 `DocumentSnapshot v2`，建立 Final Check 第一版正式 **Comparison Engine**。

本阶段的核心目标是回答：

> 基准文档与当前文档之间，究竟发生了哪些可定位、可复现、可解释的变化？

Comparison Engine 必须面向：

`DocumentSnapshot`

而不是直接依赖 DOCX 文件路径或 Open XML SDK 对象。

最终输出正式：

`ComparisonResult`

并为后续以下能力提供稳定数据基础：

- 模板 VS 当前合同
- 我方基准版本 VS 对方版本
- 双栏比对 UI
- 修改清单
- 修改状态
- 修改归并
- Excel 导出
- 格式恢复
- 历史 ComparisonRecord

---

# 2. 开始前必须重新读取

执行本任务前必须读取：

1. `/AGENTS.md`
2. `/docs/tasks/comparison-engine-v0.md`
3. `/docs/tasks/document-engine-v0.md`
4. `/docs/PRD/PRD-v0.1.0.md`
5. `/docs/architecture/document-engine.md`
6. `/docs/architecture/ADR-0001-technology-stack.md`
7. `/docs/architecture/ADR-0002-document-snapshot-schema-and-persistence.md`
8. 当前 `FinalCheck.Comparison`
9. 当前 `FinalCheck.Core`
10. 当前 Documents Snapshot 模型及相关测试

不得根据聊天记忆猜测 Snapshot 数据结构。

---

# 3. 状态恢复规则

发生：

- Context compaction
- 会话中断
- 设备切换
- 长时间执行
- Git 状态变化

时，必须：

1. 重新读取本任务文件；
2. 查看任务 Phase 状态；
3. 检查 git status / diff / recent commits；
4. 阅读当前实现；
5. 运行相关测试；
6. 从仓库实际状态恢复任务。

不得依赖上下文摘要自行判断“应该做到哪里了”。

---

# 4. 本阶段范围

Comparison Engine v0 至少正式支持：

### Structure Matching

- Paragraph matching
- Table matching
- Table cell matching
- 基础条款 / 标题辅助特征

### Text Difference

- Unchanged
- Insert
- Delete
- Replace

### Structure Change

- Paragraph added
- Paragraph deleted
- Paragraph moved
- Paragraph moved + modified

### Format Difference

- Character formatting
- Paragraph formatting
- 基础 table / cell formatting

### Revision / Comment Integration

- Word native revisions
- Word comments
- Snapshot 实际内容差异

### Output

- ComparisonResult
- ChangeItem
- NodeMapping
- Match confidence
- Diagnostics

### Grouping

- 相同重复修改的规则归并

---

# 5. 本阶段明确不做

不要扩大到：

- AI semantic grouping
- 法律风险分析
- 修改接受 / 拒绝建议
- 正式双栏 UI
- 完整格式恢复
- 精确 Word 页码
- Excel 正式导出
- 项目 / 模板中心完整 UI
- Release / Tag

允许建立后续 UI 所需的数据模型。

---

# 6. ComparisonResult 正式模型

建立正式结果模型，例如：

```text
ComparisonResult
├─ Metadata
├─ BaselineSnapshotId
├─ CurrentSnapshotId
├─ NodeMappings
├─ Changes
├─ Groups
├─ Diagnostics
└─ Statistics
```

具体类型名根据当前架构确定。

要求：

- 可序列化；
- 可持久化；
- 不保存 Open XML SDK 实例；
- 后续可绑定 ComparisonRecord；
- 不依赖 UI。

---

# 7. ChangeItem

正式建立统一修改项模型。

至少支持类型：

- TextInsert
- TextDelete
- TextReplace
- ParagraphInsert
- ParagraphDelete
- ParagraphMove
- ParagraphMoveAndModify
- CharacterFormatChange
- ParagraphFormatChange
- TableChange
- TableCellChange
- Comment
- NativeRevision

根据实际实现可以进一步细分。

每个 ChangeItem 至少保留：

- ID
- Type
- BaselineNodeId
- CurrentNodeId
- Structural location
- Baseline text
- Current text
- Difference spans
- Format difference
- Revision linkage
- Comment linkage
- Match confidence
- Diagnostic flags

不要只保存一段描述字符串。

---

# 8. Node Mapping

建立正式：

`DocumentNodeMapping`

或等价模型。

至少表达：

```text
Baseline node
↔
Current node
```

以及：

- Mapping type
- Confidence
- Match evidence
- Whether moved
- Whether modified

这是未来格式恢复的重要基础。

Mapping 与 ChangeItem 必须解耦：

> 节点可以成功 Mapping，但同时存在多个变化。

---

# 9. Phase 1 — Paragraph Matching

这是整个 Comparison Engine 最关键的阶段。

不要直接按 Paragraph index 一一比较。

需要设计多阶段匹配。

建议流程：

```text
Exact structural match
↓
Exact normalized text match
↓
Strong heading / numbering match
↓
High text similarity
↓
Neighbour / context consistency
↓
Unmatched
```

可以调整，但必须是多信号，而非单一相似度阈值。

---

## 9.1 Normalized Text

建立专门 normalization。

至少考虑：

- Word Run 分裂不应影响相同文本判断；
- 常见无意义空格差异；
- Tab / Break；
- Unicode normalization；
- 中英文空格。

但：

不要把真正有意义的空格变化全部删除。

原始文本始终保留。

---

## 9.2 Matching Signals

至少可利用：

- Paragraph normalized text
- Paragraph position
- Previous / next paragraph
- Style
- Numbering
- Heading-like characteristics
- Length
- Token similarity

后续可扩展。

---

## 9.3 Confidence

Mapping 至少具有：

- Exact
- High
- Medium
- Low

或数值 confidence。

低可信匹配不能伪装成确定结果。

必要时保留 Unmatched。

---

## Phase 1 验收

测试至少覆盖：

- 完全相同文档；
- 修改一个词；
- 插入新段落；
- 删除段落；
- 前部插入导致后续 index 全部偏移；
- 相同短段落重复出现；
- 标题 + 正文；
- 中英文混排。

完成后更新：

`Phase 1: DONE`

---

# 10. Phase 2 — Text Diff

在成功 Mapping 的 Paragraph 内进行局部文本 Diff。

不要使用全文级 Diff 替代结构匹配。

---

## 10.1 粒度

目标：

> 尽可能突出真正变化的字符 / 词，而不是整段标红。

例如：

`付款期限为30日`

→

`付款期限为60日`

应该识别为：

`30 → 60`

而不是整句 Replace。

---

## 10.2 中文与英文

Diff 必须考虑：

- 中文无天然空格；
- 英文按词；
- 数字；
- 标点；
- 中英文混合。

建议建立 Tokenizer 抽象。

不要简单使用：

`string.Split(' ')`

---

## 10.3 TextDiff abstraction

使用：

`ITextDiffService`

第三方 Diff Library 如有采用：

只能包在该接口后。

不得让第三方类型泄漏到 Core 模型。

---

## 10.4 DifferenceSpan

结果应至少保存：

- Baseline start / length
- Current start / length
- Old text
- New text
- Operation

以便未来 UI 精确高亮。

---

## Phase 2 验收

至少覆盖：

- 单字修改；
- 数字修改；
- 中文短语替换；
- 英文单词替换；
- 插入文字；
- 删除文字；
- 标点变化；
- 一段多处变化；
- Run 边界不同但实际文本相同。

完成后：

`Phase 2: DONE`

---

# 11. Phase 3 — Insert / Delete / Move

未匹配 Paragraph 不应立刻全部视为新增 / 删除。

需要尝试 Move Detection。

---

## 11.1 Move

如果相同或高度相似 Paragraph：

从位置 A

移动到位置 B

应该优先形成：

`ParagraphMove`

而不是：

- Delete A
- Insert B

---

## 11.2 Move + Modify

如果 Paragraph 移动同时文字发生小幅修改：

尽量形成：

`ParagraphMoveAndModify`

并同时保留：

- old position
- new position
- text diff

---

## 11.3 Move confidence

移动识别必须有合理 confidence。

对高度重复的合同套话：

不能仅凭相同文本武断认定具体对应关系。

上下文位置必须参与判断。

---

## Phase 3 验收

至少覆盖：

- 单段移动；
- 多段移动；
- 移动后修改一个词；
- 重复内容；
- 新增段落；
- 真正删除段落。

完成后：

`Phase 3: DONE`

---

# 12. 条款辅助识别

本阶段不要求完成最终 Clause Detection Engine。

但 Paragraph Matcher 可以合理利用：

- Numbering data
- 常见显式编号
- Heading style
- Paragraph style
- 文本开头模式

作为匹配信号。

例如：

- 第一条
- 第1条
- 1.
- 1.1
- （1）
- 一、

不得把简单正则识别结果视作绝对正确的“法律条款结构”。

将其作为：

> Matching Hint

而不是法律语义。

---

# 13. Phase 4 — Format Diff

基于 Document Engine 已计算的 Effective Formatting。

格式比较不能只看 Direct Formatting。

---

## 13.1 Character Format

至少识别：

- Font
- Font size
- Color
- Bold
- Italic
- Underline
- Strike
- Highlight

考虑四字体槽。

---

## 13.2 Paragraph Format

至少：

- Alignment
- Indent
- First line
- Hanging
- Line spacing
- Space before / after

---

## 13.3 FormatDifference

同一位置存在多个格式变化时：

形成一个 FormatDifference / ChangeItem。

例如：

```text
宋体 → 仿宋
小四 → 五号
左对齐 → 两端对齐
```

应能够作为一个格式修改项展开查看。

不要制造三个彼此无关联的顶级修改。

---

## 13.4 表格

至少建立基础 Table / Cell diff：

- Cell text
- Width
- Alignment
- Shading
- Border
- Merge state

结构变化复杂时允许降级：

> TableStructureChanged

并保留能够可靠识别的局部变化。

---

## Phase 4 验收

至少覆盖：

- 仅字体变化；
- 仅字号变化；
- 多格式同时变化；
- 文本 + 格式同时变化；
- 段落格式；
- Cell format；
- Merge change 基础识别。

完成后：

`Phase 4: DONE`

---

# 14. Word Native Revision Integration

Comparison Engine 必须区分两类证据：

### A. Native Revision

Word 本身存在：

- Insert
- Delete
- Format revision

### B. Actual Snapshot Difference

即使：

- Track Changes 被关闭；
- 修订已接受；
- 修订元数据被清理；

仍需通过实际 Snapshot 比较发现差异。

因此：

> Native Revision 是额外证据，不是 Comparison 的唯一来源。

---

## 14.1 重合变化

如果 Native Revision 与实际 Snapshot Difference 指向同一处：

优先关联到同一 ChangeItem。

不要形成明显重复的两条修改。

保存：

`SourceEvidence`

或同等信息。

---

# 15. Comment Integration

如果 Comment anchor 能够关联到某个 ChangeItem：

建立关联。

但 Comment 本身仍须可独立访问。

如果没有对应变化：

不能丢弃。

可以作为：

`CommentOnly`

或独立 Comment Result。

---

# 16. Phase 5 — Rule Grouping

MVP 不使用 AI。

先实现确定性规则归并。

例如同一 Comparison 中：

```text
甲方 → 委托方
```

重复出现 8 次。

应生成：

> 共 8 处

的 ChangeGroup。

展开后仍保留每一个实际 ChangeItem 和位置。

---

## 16.1 Group criteria

第一版只归并真正高度确定的重复修改。

例如：

- 相同 old text
- 相同 new text
- 相同 change type

不要把：

`30日→60日`

和

`付款期限延长`

仅凭语义“看起来相似”合并。

这是未来 AI Semantic Grouping 的范围。

---

## 16.2 同步

Group 只是 ChangeItem 的视图组织层。

不得复制产生另一套修改数据。

后续：

> Group view / Individual view

必须使用同一原始 ChangeItem。

---

# 17. Comparison Diagnostics

建立：

`ComparisonDiagnostics`

至少覆盖：

- AmbiguousParagraphMatch
- LowConfidenceMapping
- TableStructureFallback
- UnsupportedComparisonElement
- DuplicateCandidate
- RevisionMappingFailed

遇到不确定情况：

宁可标记低可信或无法匹配，

不要产生虚假的确定结果。

---

# 18. Comparison Statistics

至少可以统计：

- Total changes
- Text changes
- Format changes
- Paragraph added
- Paragraph deleted
- Paragraph moved
- Comments
- Group count
- Low-confidence mappings

后续 UI 会使用。

---

# 19. ComparisonResult 序列化

ComparisonResult 必须：

- Serialize
- Deserialize
- Round-trip

不要保存仅运行时有效对象。

如需要 Schema Version：

建立：

`ComparisonSchemaVersion`

并在 architecture 文档说明。

---

# 20. Persistence

本轮至少完成：

ComparisonResult

→ Serialize

→ SQLite

→ Load

→ Deserialize

→ 核心数据一致

的 Spike / 正式验证。

不要把大量 DifferenceSpan 全拆成 EF 行，除非存在非常明确的检索需求和架构依据。

优先保持可迁移 Payload 模式。

---

# 21. 测试 Fixture

继续复用 Document Engine fixture generator。

新增 Comparison Fixtures。

建议至少覆盖：

1. Identical
2. SingleWordReplace
3. NumberReplace
4. TextInsert
5. TextDelete
6. ParagraphInsert
7. ParagraphDelete
8. ParagraphMove
9. ParagraphMoveAndModify
10. MultipleChangesSameParagraph
11. DuplicateParagraphs
12. ChineseText
13. MixedChineseEnglish
14. CharacterFormattingChange
15. ParagraphFormattingChange
16. TextAndFormatChange
17. TableCellTextChange
18. TableFormatChange
19. RevisionTrackedChange
20. AcceptedRevisionButActualDifference
21. CommentOnChange
22. CommentWithoutChange
23. RepeatedReplacementGrouping

全部使用非敏感、小型测试文档。

---

# 22. 性能

Comparison Engine 仍需保持轻量化。

测试至少记录：

- Snapshot A size
- Snapshot B size
- Paragraph count
- Comparison time
- Managed allocations
- ChangeItem count

建立：

- small fixture
- medium synthetic document

两档。

不要在本轮生成超大型仓库 fixture。

Medium synthetic document 可以运行时生成。

---

# 23. 算法复杂度

特别注意 Paragraph Matching。

禁止无控制地对：

> A 中全部 Paragraph × B 中全部 Paragraph

执行高成本字符串比较。

对于长合同需要有：

- candidate reduction
- indexing
- exact-match fast path
- length / position filters

或其他合理机制。

在 architecture 文档说明复杂度控制策略。

---

# 24. Determinism

相同 Snapshot A + Snapshot B：

多次运行 Comparison Engine：

必须得到相同：

- NodeMapping
- ChangeItem
- Grouping

除非代码版本 / schema 明确变化。

禁止随机 tie-breaking。

出现多个相同候选时：

使用稳定规则解决或标记 ambiguous。

---

# 25. Cancellation

Comparison 应支持：

`CancellationToken`

尤其：

- Paragraph matching
- Text diff
- Table processing

不得让长合同 Compare 无法取消。

---

# 26. Progress

预留阶段进度：

- Preparing
- Matching structure
- Comparing text
- Detecting moves
- Comparing formatting
- Processing revisions/comments
- Grouping changes
- Completed

不要求虚假的精确百分比。

---

# 27. UI 边界

本轮不要开发正式比对 UI。

如果需要开发验证：

允许 Debug 页面展示：

- Changes 数量
- Mapping 数量
- Group 数量
- Change type
- old/new text
- confidence

Release 下不作为正式用户功能。

---

# 28. Architecture Documentation

新增：

`docs/architecture/comparison-engine.md`

至少说明：

- Comparison pipeline
- Node mapping
- Paragraph matching
- Normalization
- Text tokenizer / diff
- Move detection
- Format diff
- Revision integration
- Comment integration
- Rule grouping
- Confidence
- Diagnostics
- Determinism
- Performance strategy
- Known limitations

---

# 29. ADR

如果确定重大算法或持久化方案：

新增 ADR。

例如：

- Paragraph matching strategy
- Text diff library
- Comparison result schema

不要为普通实现细节创建大量 ADR。

---

# 30. 每个 Phase 执行纪律

每完成一个 Phase：

1. 完成该 Phase 的实现；
2. 运行该 Phase 要求的相关测试与检查；
3. 检查已有能力是否回归；
4. 更新本文中的 Phase 状态，并记录实际完成内容和测试结果；
5. 创建一个逻辑完整、可理解且与该 Phase 实际内容一致的 commit；
6. 正常 push 到远程仓库并确认成功；
7. 再进入下一 Phase。

上述纪律适用于 Phase 1 至 Phase 5。每个 Phase 都应形成独立、可理解的开发里程碑；不要五个阶段完成后才首次测试或统一 push，也不要为了很小的编辑频繁 commit。

Commit message 应按实际 Phase 内容命名，不固定重复同一条信息。例如可以参考：

- `feat: implement paragraph matching`
- `feat: implement text diff`
- `feat: detect paragraph structural changes`
- `feat: implement format comparison`
- `feat: integrate revisions comments and grouping`

以上仅为示例，实际 message 以该 Phase 的实现内容为准。每个 Phase push 成功后才能开始下一 Phase。

## 30.1 跨设备中途切换

如果 Phase 尚未完成，但需要切换电脑继续：

1. 优先形成可 build、可测试且不会破坏正常分支的安全 checkpoint；
2. 将当前 Phase 标记为 `IN PROGRESS`；
3. 在本文记录：
   - 已完成内容；
   - 剩余内容；
   - 当前测试结果；
   - 当前已知问题；
4. 若当前分支状态可安全同步，可以使用类似 `chore: checkpoint comparison phase X` 的 commit 并正常 push；
5. 如果当前工作明显不完整或不能安全留在主分支，则创建临时 work 分支后 push，不得把坏代码直接推送到主分支。

在另一台电脑继续前，必须 fetch / pull 最新远程状态，重读 `AGENTS.md` 和本文，检查 recent commits 与 `git status`，再以仓库实际状态继续，不依赖聊天记忆。

---

# 31. 当前进度

由 Codex 持续维护：

- Phase 1 — Paragraph Matching：TODO
- Phase 2 — Text Diff：TODO
- Phase 3 — Insert/Delete/Move：TODO
- Phase 4 — Format Diff：TODO
- Phase 5 — Revision/Comment/Grouping/Persistence：TODO

可以使用：

- TODO
- IN PROGRESS
- DONE
- BLOCKED

BLOCKED 时写明原因。

---

# 32. 最终验收

至少执行：

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

并确认：

- 所有原 Document Engine Tests 继续通过；
- Comparison Tests 全部通过；
- App 可启动；
- Windows CI 成功；
- macOS CI 成功；
- Core / Documents / Comparison / Data 无新增 Windows-only dependency。

---

# 33. Git

Phase 1 至 Phase 5 按第 30 节分别创建有意义的 commit，并在进入下一 Phase 前正常 push。

整个 Comparison Engine v0 完成后，如仍有文档整理、验收修正或其他逻辑完整的剩余改动，可以再创建一个内容明确的最终整理 commit 并正常 push；没有实际改动时不要为了形式创建空 commit。

禁止：

- force push
- amend 已推送历史
- reset --hard
- Tag
- Release

版本继续：

`v0.1.0 development`

---

# 34. 完成反馈

## Comparison Engine v0

### Result Model

- Comparison schema：
- ChangeItem：
- NodeMapping：
- Diagnostics：
- Serialization：
- Persistence：

### Matching

- Exact：
- Similar：
- Context：
- Duplicate：
- Confidence：

### Text Diff

- Chinese：
- English：
- Number：
- Insert/Delete/Replace：
- Difference spans：

### Structure

- Paragraph insert：
- Paragraph delete：
- Paragraph move：
- Move + modify：

### Formatting

- Character：
- Paragraph：
- Table / Cell：

### Native Word Data

- Revision integration：
- Comment integration：

### Grouping

- Exact repeated change：
- Individual item preservation：

### Tests

- Total：
- Passed：
- Failed：
- Fixtures：

### Performance

- Small：
- Medium：
- Allocation：
- Complexity controls：

### Platform

- Windows：
- macOS：
- Windows-only dependencies：

### Documentation

- comparison-engine.md：
- ADR：

### Git

- Commit：
- Push：
- Working tree：

### Known Limitations

明确列出：

- 暂未支持；
- 低可信场景；
- 降级策略。

最终判断：

> Comparison Engine v0 是否已经具备进入 Format Restore Engine v0 和正式 Comparison UI 开发阶段的稳定基础。
