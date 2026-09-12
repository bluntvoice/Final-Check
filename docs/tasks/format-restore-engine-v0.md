# Final Check — Format Restore Engine v0

> Status: In Progress
> Version: v0.1.0 development
> Scope: Format Restore Engine
> Depends on: Document Engine v0, Comparison Engine v0
> Last updated: 2026-09-12

---

## 1. 任务目标

建立 Final Check 第一版正式 **Format Restore Engine**。

核心能力：

> 基于标准模板 / 基准文档与当前修订文档之间已经建立的 `DocumentNodeMapping`，仅恢复当前文档的格式，不恢复或覆盖当前文档的文字内容。

例如：

模板：

> 宋体、小四、30日

当前修订版本：

> 仿宋、五号、60日

恢复后：

> 宋体、小四、60日

也就是说：

> **格式回到模板，内容保留当前版本。**

---

## 2. 核心安全目标

任何格式恢复操作都必须同时满足：

1. 不恢复旧文字；
2. 不删除当前新增文字；
3. 不恢复当前已删除文字；
4. 保留 Word Insert/Delete 修订；
5. 保留批注；
6. 不生成新的格式修订痕迹；
7. 不修改用户原始 DOCX；
8. 不执行 Accept All Revisions；
9. 不破坏未处理的 Word 内容结构；
10. 恢复后必须可以重新解析和验证。

这是本阶段最重要的验收标准。

---

## 3. 开始前必须重新读取

执行前必须读取：

1. `/AGENTS.md`
2. `/docs/tasks/format-restore-engine-v0.md`
3. `/docs/tasks/document-engine-v0.md`
4. `/docs/tasks/comparison-engine-v0.md`
5. `/docs/PRD/PRD-v0.1.0.md`
6. `/docs/architecture/document-engine.md`
7. `/docs/architecture/comparison-engine.md`
8. `/docs/architecture/ADR-0001-technology-stack.md`
9. `/docs/architecture/ADR-0002-document-snapshot-schema-and-persistence.md`
10. `/docs/architecture/ADR-0003-comparison-model-and-paragraph-matching.md`
11. 当前 Documents / Comparison / Core / Data / Infrastructure 代码和测试

不得根据聊天记忆推测已有模型。

---

## 4. 状态恢复与多设备规则

严格遵守 `AGENTS.md`：

每完成一个 Phase：

1. 完成实现；
2. 运行阶段测试；
3. 更新本任务文件状态和实际完成说明；
4. 创建独立、有意义的 commit；
5. 正常 push；
6. push 成功后再进入下一 Phase。

上下文压缩、会话中断或换电脑时：

以远程仓库、任务文件、Git 状态、代码和测试为准。

---

# 5. 本阶段明确不做

本轮不扩大到：

* 正式 Comparison UI
* 模板中心完整 UI
* 项目 / 版本时间线
* Excel 导出
* AI
* 正式软件内 updater UI
* macOS 安装包
* Stable Release

允许为 Format Restore 编写 Debug / Developer 验证工具。

---

# 6. Phase 1 — Restore Model / Plan / Mapping

## 6.1 Restore Plan

建立正式模型，例如：

`FormatRestorePlan`

至少包含：

* PlanId
* Source document identity
* Baseline / Template snapshot identity
* Current snapshot identity
* Mapping reference
* CreatedAt
* RestoreItems
* Diagnostics
* Status

具体命名可结合现有架构调整。

---

## 6.2 Restore Item

每个 RestoreItem 应至少能够表达：

* RestoreItemId
* BaselineNodeId
* CurrentNodeId
* Node type
* Location
* Current formatting
* Target formatting
* Difference
* Restore category
* Restore eligibility
* Fallback source（如有）
* Confidence
* Diagnostic

Restore category 至少预留：

* Character
* Paragraph
* Table
* Cell

---

## 6.3 Restore Plan 与执行分离

必须采用：

```text
Analyze
↓
Generate Restore Plan
↓
Review / Validate
↓
Execute
```

禁止：

> 在分析格式差异时立即直接写 DOCX。

这样未来 UI 才能实现：

> 恢复前预览“当前位置 / 当前格式 / 目标格式”。

---

## 6.4 Mapping

优先使用 Comparison Engine 已有的：

`DocumentNodeMapping`

或其正式等价物。

禁止再在 Format Restore 中建立一套互相冲突的段落匹配算法。

允许针对格式恢复建立额外：

`RestoreNodeMapping`

但必须引用原始 Comparison Mapping，并说明为什么需要额外映射。

---

## 6.5 低可信 Mapping

如果 Mapping confidence 不足：

不能静默执行高风险恢复。

应：

* 标记为 NeedsReview；
* 或拒绝自动恢复。

Format Restore 应比 Comparison 更保守。

---

## Phase 1 验收

至少测试：

* 字符 RestoreItem 生成；
* 段落 RestoreItem 生成；
* 多项格式差异合并；
* Unmatched 节点；
* Low confidence；
* Plan 可序列化；
* 相同输入产生确定性 Plan。

完成后：

`Phase 1 — DONE`

commit + push。

建议 commit：

`feat: add format restore planning model`

---

# 7. Phase 2 — Character Formatting Restore

实现字符级格式恢复。

---

## 7.1 MVP 范围

至少支持：

* ascii font
* hAnsi font
* eastAsia font
* cs font
* Font size
* Color
* Bold
* Italic
* Underline
* Strike
* Highlight

---

## 7.2 基本原则

必须：

> 保留 Current Run 的文字，仅修改目标格式属性。

例如：

```text
Baseline Run:
text = "30日"
font = 宋体

Current Run:
text = "60日"
font = 仿宋

Restored:
text = "60日"
font = 宋体
```

---

## 7.3 Run 边界不同

模板：

```text
[付款期限为30日]
```

当前：

```text
[付款期限为][60日]
```

不能要求 Run 边界完全一致才能恢复。

应基于：

* NodeMapping
* DifferenceSpan
* Text position
* Effective format
* Context

建立安全恢复策略。

如果无法可靠对应：

不得暴力重构整个 Paragraph。

可降级：

* 部分恢复；
* NeedsReview；
* Diagnostic。

---

## 7.4 新增文字

如果当前版本新增文字，没有模板中的直接 Run 对应：

优先采用：

> 同一 Paragraph / 同层级周边文本的目标样式。

Fallback 顺序应确定、可测试。

建议考虑：

1. 对应 Paragraph baseline style；
2. 当前 Paragraph 中邻近已有映射 Run；
3. 同级条款默认格式；
4. template-specific style；
5. global style；
6. 无法判断则不自动恢复。

最终策略记录到 architecture 文档。

---

## 7.5 Direct vs Effective Formatting

当前 Document Engine 已支持 Effective Formatting。

但写回 DOCX 时：

不能简单把所有 Effective Formatting 全部写成 Direct Formatting。

否则可能：

* 文件膨胀；
* 破坏 Style；
* 以后样式无法统一调整。

需要区分：

> 应恢复 Style reference

与

> 必须写 Direct Formatting

优先保持文档原有样式体系。

---

## Phase 2 验收

至少覆盖：

* 字体恢复；
* 中文/英文字体槽；
* 字号；
* 颜色；
* 粗体；
* 下划线；
* 多属性同时恢复；
* 内容变化但格式恢复；
* 新增文字 fallback；
* Run 边界不一致；
* Revision 中的 Run。

完成后：

`Phase 2 — DONE`

commit + push。

建议 commit：

`feat: restore character formatting safely`

---

# 8. Phase 3 — Paragraph Formatting Restore

至少支持：

* Alignment
* Left indent
* Right indent
* First line indent
* Hanging indent
* Line spacing
* Line rule
* Space before
* Space after

---

## 8.1 原则

只恢复 Paragraph Properties。

禁止：

* 替换整个 Paragraph XML；
* 用 baseline Paragraph 覆盖 current Paragraph；
* 复制 baseline 正文。

---

## 8.2 Style

需要合理处理：

* ParagraphStyleId
* BasedOn
* Direct Paragraph Formatting

优先恢复为与模板一致且语义合理的 Style / Properties 组合。

避免为了视觉一致：

> 把每一个 Effective Property 都写成 Direct Formatting。

---

## 8.3 新增 Paragraph

如果 current 新增 Paragraph 没有 baseline 直接对应：

依据 PRD：

> 自动优先采用同级条款模板样式。

需要建立确定性 fallback。

可以利用：

* previous / next mapped paragraph；
* Numbering level；
* Heading / paragraph style；
* Clause hints；
* 周边结构。

低可信时：

不自动恢复或标记 NeedsReview。

---

## Phase 3 验收

至少：

* Alignment；
* Indent；
* First line；
* Hanging；
* Spacing；
* Line spacing；
* Style restore；
* 新增 Paragraph fallback；
* 文本内容不变化。

完成后：

`Phase 3 — DONE`

commit + push。

建议：

`feat: restore paragraph formatting`

---

# 9. Phase 4 — Table / Cell Formatting Restore

这是本阶段重点风险项之一。

当前 Comparison Engine 已知限制：

> Table / Cell Snapshot 主要提供 direct formatting。

因此不要假装已经拥有完整 Word 最终有效表格样式。

---

## 9.1 先明确能力边界

在实现前检查现有 Snapshot 能否可靠恢复：

* Width
* Alignment
* Shading
* Borders
* Row height
* Cell formatting
* Grid span
* Vertical merge

如果某些格式依赖复杂 Table Style / conditional style：

必须区分：

* Reliable
* Partial
* Unsupported

不能静默生成错误格式。

---

## 9.2 表格恢复原则

禁止：

> 用模板整个 Table XML 替换当前 Table。

因为当前表格可能：

* 修改文字；
* 增删行列；
* 修改单元格；
* 合并/拆分；
* 带修订/批注。

应基于：

`Table / Cell NodeMapping`

逐项恢复。

---

## 9.3 结构变化

如果 Table 结构已经明显变化：

只恢复可以可靠 Mapping 的内容。

例如：

* 对应 Cell 的底纹；
* 对应 Cell 的边框；
* 当前存在的列宽。

不得为了恢复格式：

> 把新增行列删除。

---

## 9.4 Merge / Split

合并/拆分属于结构变化。

Format Restore 默认不能把：

> 当前结构变化

自动恢复成模板结构，

除非未来用户明确执行：

> 结构恢复

而本项目当前定义的是：

> 格式恢复。

所以本阶段：

* 检测；
* 展示；
* Diagnostic；

但不要把合并/拆分当普通格式强制恢复。

---

## Phase 4 验收

至少：

* Table alignment；
* Cell shading；
* Border；
* Cell width；
* Row height（如果现有模型支持）；
* 结构不同情况下局部恢复；
* 合并单元格安全处理；
* Table text 完全不变。

完成后：

`Phase 4 — DONE`

commit + push。

建议：

`feat: restore table and cell formatting`

---

# 10. Phase 5 — Revision Preservation / Working Copy / Undo / Persistence

这是最终安全阶段。

---

## 10.1 原始文件永远不修改

PRD 已明确：

Final Check 只记录原始 DOCX 路径。

不得：

* 修改原始文件；
* 设置原始文件 readonly；
* 偷偷复制后替换原文件。

执行 Format Restore 时：

必须生成或更新独立：

> Format Restored Working Copy

---

## 10.2 Working Copy

一个 ContractVersion 最多维护一个当前：

> restored working file

连续多次格式恢复：

更新该 working copy。

不要每点击一次恢复就自动生成：

```text
restore-1.docx
restore-2.docx
restore-3.docx
...
```

内部恢复历史通过操作记录管理。

---

## 10.3 首次恢复

首次恢复：

```text
Original Current Version
↓
Create Working Copy
↓
Apply Restore
↓
Validate
↓
Persist metadata
```

原文件不变。

---

## 10.4 后续恢复

继续操作：

```text
Current Working Copy
↓
Apply next Restore operation
↓
Validate
↓
Update Working Copy
```

---

# 11. Revision Preservation

恢复后必须验证：

### Text Revision

* Insert 数量/内容保持；
* Delete 数量/内容保持。

### Comment

* 批注数量保持；
* 批注正文保持；
* Anchor 尽量保持。

### Text

当前版本可见/逻辑文本保持。

---

# 12. 禁止创建新的格式修订

这是硬性验收条件。

恢复格式本身不得新增：

* `w:rPrChange`
* `w:pPrChange`
* `w:tblPrChange`
* `w:trPrChange`
* `w:tcPrChange`

或等价格式修订节点。

即使文档启用了：

> Track Changes

格式恢复也必须直接修改格式状态，而不是创建新的“格式修订”。

---

## 12.1 测试方式

测试 Fixture 应包含：

* Track Changes metadata；
* 已存在文本修订；
* 已存在格式修订；
* Comment。

执行恢复前后统计相关 Open XML 节点。

验证：

> Format Restore 没有增加新的 Format Revision Node。

---

# 13. Undo Last Restore

MVP 只需要支持：

> 撤销最近一次格式恢复操作。

不是无限 Undo Stack。

---

## 13.1 Undo 模型

每次 Format Restore operation 必须保存足够信息：

* OperationId
* AppliedAt
* Affected nodes
* Before formatting
* After formatting
* Working file hash before
* Working file hash after

Undo 时：

只恢复格式状态。

不得恢复文字。

---

## 13.2 Undo 安全检查

执行 Undo 前检查：

> 当前 Working Copy hash

是否等于该操作保存的：

> after hash。

如果不同：

说明可能被外部修改。

不能直接 Undo。

进入 External Modification 处理。

---

# 14. 外部编辑检测

用户可能用 Word / WPS 打开 Restored Working Copy 修改。

Final Check 必须通过 SHA-256 检测。

如果：

> 保存的 hash != 当前文件 hash

提示上层：

`WorkingCopyExternallyModified`

---

## 14.1 后续正式 UI 行为

预留两种选择：

### Preserve External Changes

保留外部编辑结果：

* 重新 parse；
* 重新生成 Snapshot；
* 重新 Comparison；
* 更新 working version 状态。

原历史 Comparison 不覆盖。

### Regenerate

从原始 current contract 重新生成 working copy。

本阶段可以先实现底层 service / result，不要求正式 Dialog UI。

---

# 15. Restore Operation Persistence

正式保存：

* Restore Plan
* Restore Operation
* Restore Items
* Working Copy metadata
* Before / After hashes
* Undo status
* CreatedAt
* CompletedAt
* Diagnostics

数据库 schema 如需升级：

必须有 migration。

升级测试要确保：

* DocumentSnapshot 历史不丢；
* ComparisonResult 不丢。

---

# 16. Reparse Validation

每次成功恢复后：

必须使用正式 Document Engine：

> 重新解析输出 DOCX。

不能只相信写入没有报异常。

---

## 16.1 验证至少包括

恢复前后：

* Logical text equality；
* Revision preservation；
* Comment preservation；
* Paragraph count 合理；
* Table count 合理；
* Target formatting 已生效；
* 没有新增 format revisions；
* Package 可正常重新打开。

如验证失败：

操作视为失败。

不能标记 Completed。

---

# 17. Atomic File Write

Working Copy 写入必须尽量采用安全流程：

```text
temporary file
↓
write
↓
validate
↓
atomic replace / safe move
```

避免应用崩溃导致 Working Copy 只剩半个 ZIP Package。

不得：

> 直接在唯一 Working Copy 上边写边改。

---

# 18. 文件名

内部 Working Copy 命名应稳定、可识别。

不要将临时操作序号大量暴露为用户文件。

具体内部命名由实现决定。

未来用户手动导出时：

默认建议：

`原文件名-格式已恢复.docx`

但正式导出 UI 不属于本阶段。

---

# 19. Restore Scope

引擎必须支持至少三种 Scope：

* All
* Category
* SelectedItems

Category 至少：

* Character
* Paragraph
* Table/Cell

这样以后 UI 可以支持：

* 一键全部恢复；
* 只恢复字体；
* 只恢复段落；
* 勾选部分恢复。

---

# 20. Idempotency

同一个 Restore Plan：

执行一次后已经达到 Target Formatting。

再次分析：

不应该重复产生相同 RestoreItem。

即：

> Restore 应尽量具备幂等性。

建立测试。

---

# 21. Determinism

相同：

* Baseline Snapshot
* Current Snapshot
* NodeMapping
* Restore policy

应生成相同 Restore Plan。

禁止随机 fallback。

---

# 22. Cancellation

Plan generation 和 Restore execution 都应支持：

`CancellationToken`

如果写文件阶段取消：

必须保证：

* 原文件不变；
* 旧 Working Copy 不损坏。

---

# 23. Progress

预留阶段：

### Plan

* Preparing
* Resolving mappings
* Resolving target formatting
* Validating plan
* Completed

### Execute

* Preparing working copy
* Applying character formatting
* Applying paragraph formatting
* Applying table formatting
* Saving
* Reparsing
* Validating
* Completed

---

# 24. Diagnostics

建立正式：

`FormatRestoreDiagnostics`

示例：

* LowConfidenceMapping
* UnmappedNode
* UnsupportedTableStyle
* AddedTextFallbackUsed
* AddedParagraphFallbackUsed
* ExistingFormatRevisionPreserved
* ExternalModificationDetected
* ReparseValidationFailed
* WorkingCopyWriteFailed
* UndoHashMismatch

不确定时：

宁可跳过恢复并产生 Diagnostic，

不能猜测性破坏文档。

---

# 25. Test Fixtures

继续使用非敏感、小型 DOCX。

新增至少覆盖：

1. CharacterFontRestore
2. FontSizeRestore
3. MultipleCharacterProperties
4. ChineseEnglishFonts
5. ParagraphAlignment
6. ParagraphIndent
7. ParagraphSpacing
8. LineSpacing
9. StyleBasedFormatting
10. DirectFormatting
11. ChangedTextSameNode
12. ChangedRunBoundary
13. AddedText
14. AddedParagraph
15. RevisionInsertPreservation
16. RevisionDeletePreservation
17. CommentPreservation
18. ExistingFormatRevision
19. TableFormatting
20. CellFormatting
21. TableStructureChanged
22. TrackChangesEnabled
23. UndoLastRestore
24. ExternalWorkingCopyModification
25. IdempotentRestore

---

# 26. Golden Tests

对关键场景建立少量：

> Before / After semantic assertions

不要仅比较整个 DOCX binary hash。

DOCX ZIP 本身可能因元数据不同导致 hash 改变。

应该验证：

* 文本；
* XML 节点；
* format；
* revisions；
* comments；
* structure。

---

# 27. 性能

至少建立：

### Small

小型合同 Fixture。

### Medium

程序化中型合同。

记录：

* Plan generation time
* Restore execution time
* Reparse validation time
* Managed allocations
* Working Copy file size

本阶段性能目标主要是：

> 不出现明显指数级或无法接受的回写开销。

---

# 28. 体积控制

不得为了 Format Restore 引入大型商业 Word SDK。

继续使用：

> Open XML SDK

除非发现无法满足核心需求，且必须形成架构级讨论后向用户确认。

---

# 29. 跨平台

Format Restore Core Engine 应尽可能保持跨平台。

不得依赖：

* Word.exe
* COM
* Office Interop
* Registry

Windows 仍是官方主要支持平台。

macOS CI 必须继续通过 Core/Documents/Comparison/Data 兼容性验证。

---

# 30. Debug 验证

如需要，可以增加 Debug-only 工具：

* 选择 baseline DOCX
* 选择 current DOCX
* 生成 Restore Plan
* 查看 RestoreItems
* 执行
* 输出 Working Copy
* 查看验证结果

不要把它包装成正式业务 UI。

---

# 31. Architecture Documentation

新增：

`docs/architecture/format-restore-engine.md`

至少说明：

* Restore pipeline
* Restore Plan
* Mapping usage
* Character restore
* Paragraph restore
* Table restore
* Added content fallback
* Working Copy
* Atomic write
* Revision preservation
* Comment preservation
* Format revision policy
* Undo
* External modification
* Reparse validation
* Diagnostics
* Known limitations

---

# 32. ADR

如果产生重大架构决策：

例如：

* Working Copy storage model
* Undo persistence model
* Formatting write strategy

可新增 ADR。

不要为普通代码实现创建大量 ADR。

---

# 33. Phase 状态

由 Codex 维护：

* Phase 1 — Restore Plan / Mapping：DONE
  - 实际完成（2026-09-12）：schema 1 纯领域 Plan/Item、Scope/Policy、进度与诊断；Snapshot-only planner 复用正式 ComparisonNodeMapping，不另建匹配算法；合并字符/段落属性差异，严格 Exact/High + score ≥ 0.8，未匹配/混合 Run 与低可信诊断，身份校验、确定性 ID/时间与 JSON round-trip。
  - 测试：Phase 1 新增 5 项全部通过，Release 全部 86/86（Comparison 52、Documents 26、Data 4、Core 2、App 2）。原样任务规范保存后已比对附件一致，再仅更新维护状态。
* Phase 2 — Character Formatting：DONE
  - 实际完成：四字体槽/字号/颜色/粗斜体/下划线/删除线/高亮最小属性写回；UTF-16 Diff 位置投影、统一段落格式与邻近一致格式的新增文字 fallback；Run 边界不同仍可恢复，跨冲突目标保守跳过；私有流写回并正式重解析，文本/修订/批注/OPC 原样内容验证、Scope、取消和 hash 拒绝。
  - 测试：新增 6 个字符恢复测试覆盖所有支持属性、改变内容、Run 边界、mixed format、Insert/Delete/comment/existing rPrChange/Track Changes、继承样式去除 override、选择范围、取消与 stale hash；Release 全部 92/92 通过（Documents 32）。
* Phase 3 — Paragraph Formatting：DONE
  - 实际完成：pPr 属性级对齐、左右/首行/悬挂缩进、段前后、行距/rule；兼容 Style reference 与最小 direct override，不复制段落/正文/样式库；新增段落按同容器同级前后可信 mapping 目标一致 fallback，缺失样式和不足证据诊断。
  - 测试：新增 6 个段落用例覆盖首行/悬挂、完整间距、兼容样式无 flatten、缺失样式保守 override、新增段落 fallback/拒绝；Release 98/98 通过（Documents 38）。
* Phase 4 — Table / Cell Formatting：DONE
  - 实际完成：Table/Cell direct 宽度、对齐、底纹、六边框与可靠 row height/rule；Cell child 文本格式基于可信 parent mapping 派生；结构改变仅唯一未改文字锚点局部恢复，合并状态与新增行列保留；复杂 conditional/effective table style 明确 Partial/Unsupported。
  - 测试：新增 4 项 table/cell/row、改变正文、增行局部恢复/拒绝、merge state、样式边界测试；Release 全部 102/102 通过（Documents 42），table text 与未处理 OPC 内容保持。
* Phase 5 — Working Copy / Undo / Preservation：DONE
  - 实际完成：每版本稳定 working path；私有输出与 candidate 正式重解析、hash/语义保留校验、Prepared journal + 原子 replace/safe move + DB transaction；发布失败回滚/不确定状态保留，crash recovery；仅最近一次属性 Undo 且 hash/after 状态保护；外部编辑明确 preserve（追加 Snapshot/Comparison）或 regenerate（保留旧 backup）；schema 3 migration、版本化 Plan/operation/Items/history 与并发 metadata。
  - 测试：新增 19 个 Workflow 用例及 3 个 Documents 安全用例；五类格式修订、Insert/Delete/comment/Track Changes 在恢复和 Undo 后保留，格式修订新增 0；取消中写、锁冲突、坏 candidate、DB 提交前失败回滚/提交后异常不回滚、journal before/after/unknown 分支、scope/idempotency、真实可信修改文字恢复与 Medium 拒绝、迁移历史/未知 schema 均通过。
  - 本地最终：dotnet restore / Release build（0 warning / 0 error）/ test 124/124（Core 2、Documents 45、Comparison 52、Data 23、App 2）；version/release 隔离脚本与 actionlint 通过。Debug-only 隔离 AppData 启动响应正常/正常关闭，无业务 UI；正常用户数据库未用于实验。性能数据见 development/performance-baseline.md。最后 Windows/macOS CI 与一次 Test Build 的结果在 push 后补记，不创建 Tag/Release。

允许：

* TODO
* IN PROGRESS
* DONE
* BLOCKED

---

# 34. 每 Phase 验收纪律

每个 Phase：

1. 实现；
2. 运行新增测试；
3. 运行相关已有回归测试；
4. 更新本 task；
5. commit；
6. push；
7. 确认远程成功；
8. 再进入下一 Phase。

---

# 35. 全阶段最终测试

至少：

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

并确认：

* Document Engine tests 全部通过；
* Comparison Engine tests 全部通过；
* Format Restore tests 全部通过；
* App 启动正常；
* Windows CI 通过；
* macOS core CI 通过。

---

# 36. 安装包冒烟测试

现有 Windows Test Build 基础设施已经可用。

Format Restore Engine 完成后：

允许手动触发一次：

> Build Windows test installer

目的是确认新增引擎没有破坏：

* publish；
* Velopack；
* Setup；
* Portable。

不创建 Tag / Release。

如果 Test Build 成功，可记录 Artifact。

---

# 37. Git

Phase 建议 commit：

### Phase 1

`feat: add format restore planning model`

### Phase 2

`feat: restore character formatting safely`

### Phase 3

`feat: restore paragraph formatting`

### Phase 4

`feat: restore table and cell formatting`

### Phase 5

`feat: add safe format restore working copy workflow`

仅为建议。

实际 message 应准确反映代码内容。

禁止：

* force push
* amend 已推送历史
* reset --hard
* Tag
* Release

---

# 38. 最终报告

## Format Restore Engine v0

### Restore Model

* Restore plan：
* Restore items：
* Mapping：
* Confidence：
* Diagnostics：

### Character Formatting

* Fonts：
* Font size：
* Color：
* Bold/Italic：
* Underline/Strike：
* Highlight：
* Added text fallback：

### Paragraph Formatting

* Alignment：
* Indent：
* Spacing：
* Line spacing：
* Style：
* Added paragraph fallback：

### Table / Cell

* Alignment：
* Width：
* Shading：
* Border：
* Row height：
* Merge handling：
* Known effective-format limitation：

### Safety

* Original DOCX untouched：
* Text preserved：
* Revisions preserved：
* Comments preserved：
* New format revisions created：
* Reparse validation：

### Working Copy

* Creation：
* Update：
* Atomic write：
* Hash：
* External modification：
* Regenerate：

### Undo

* Last-operation undo：
* Hash validation：

### Persistence

* Database migration：
* Restore history：
* Existing Snapshot preserved：
* Existing Comparison preserved：

### Tests

* Total：
* Passed：
* Failed：
* Fixtures：

### Performance

* Plan：
* Execute：
* Reparse：
* Allocations：
* Working file size：

### Platform

* Windows：
* macOS CI：
* Windows-only dependencies：

### Packaging

* Test Build：
* Setup：
* Portable：

### Documentation

* format-restore-engine.md：
* ADR：

### Git

* Phase commits：
* Push：
* Working tree：

### Known Limitations

明确列出仍未支持、仅部分支持以及需要未来 UI 人工确认的场景。

最终判断：

> Format Restore Engine v0 是否已经具备进入正式 Comparison UI / 产品功能层开发阶段的稳定基础。
