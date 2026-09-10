# Final Check — Document Engine v0

> Status: Completed
> Version: v0.1.0 development
> Scope: Document Engine
> Last updated: 2026-09-10

---

## 1. 任务目标

将工程初始化阶段已经验证通过的 DOCX Spike 升级为正式、稳定、可测试、可扩展的 **Document Engine v0**。

本阶段的最终产物不是完整的“合同差异比较功能”，而是一个可靠的：

`DOCX → DocumentSnapshot`

解析体系。

后续以下功能都必须建立在该 Snapshot 之上：

- Comparison Engine
- 条款 / 段落匹配
- 字符级 Diff
- Word 修订识别
- 批注关联
- 格式 Diff
- 表格 Diff
- 格式恢复
- 文档预览
- 历史结构化快照

---

## 2. 开始前必须读取

执行本任务前必须重新读取：

1. `/AGENTS.md`
2. `/docs/PRD/PRD-v0.1.0.md`
3. `/docs/architecture/README.md`
4. `/docs/architecture/ADR-0001-technology-stack.md`
5. `/docs/development/README.md`
6. 当前 `FinalCheck.Documents`
7. 当前 `FinalCheck.Core`
8. 当前 `FinalCheck.Data`
9. 当前相关测试代码

不得仅依赖聊天上下文或之前的执行记忆判断当前实现状态。

---

## 3. 长任务状态恢复规则

本任务可能经历：

- 上下文压缩；
- Codex 会话中断；
- 设备切换；
- 用户中途暂停；
- Git 状态变化。

任何时候无法确定当前进度时：

1. 重新读取本任务文件；
2. 查看 `git status`；
3. 查看当前 diff；
4. 查看最近提交；
5. 查看已有实现；
6. 运行相关测试；
7. 根据实际仓库状态重新确定下一步。

禁止：

- 凭记忆猜测哪些步骤已经完成；
- 重复已经完成的实现；
- 因上下文不完整跳过尚未完成的事项。

---

# 4. 本阶段范围

Document Engine v0 至少需要建立以下正式能力：

### 文档结构

- Document
- Section
- Paragraph
- Run
- Table
- Row
- Cell

### 文本

- 普通文本
- 多 Run 文本
- Tab
- Break / Carriage Return

### Word 元数据

- Styles
- Numbering
- Comments
- Revisions
- Document Protection
- Header / Footer 基础信息
- Hyperlink 基础信息

### 格式

- 字符格式
- 段落格式
- 基础表格格式
- Direct Formatting
- Effective Formatting

### 基础设施

- Stable Node Identity
- Snapshot Schema Version
- Serialization
- SQLite Snapshot Persistence
- Parse Diagnostics
- Cancellation
- Progress
- 明确错误类型

---

# 5. 明确不属于本阶段

本轮不得扩大为：

- 完整 Comparison Engine
- 完整条款智能识别
- 条款移动算法
- 修改归并
- 正式双栏比对 UI
- 格式恢复
- Excel 修改清单
- 模板中心
- 项目 / 版本时间线
- AI
- 正式安装包
- Release
- Tag

允许为了验证 Snapshot 可比较性编写极少量辅助逻辑，但不得借机提前开发完整 Comparison Engine。

---

# 6. Phase 1 — DocumentSnapshot 正式模型

## 目标

把当前 Spike Snapshot 升级为正式领域模型。

至少能够表达：

```text
DocumentSnapshot
├─ Metadata
├─ Sections
├─ Paragraphs
│  └─ Runs
├─ Tables
│  ├─ Rows
│  │  └─ Cells
├─ Styles
├─ Numbering
├─ Revisions
├─ Comments
└─ ParseDiagnostics
```

实际结构应遵循 WordprocessingML，而不是机械照搬上图。

## 必须完成

### Stable Node Identity

Paragraph / Run / Table / Row / Cell 等节点需要：

- Snapshot 内唯一标识；
- 父节点关系；
- 结构位置；
- 可序列化；
- 后续可用于 `DocumentNodeMapping`。

不得把 Open XML SDK 对象直接保存在 Snapshot 中。

### Snapshot Schema Version

正式定义：

`SnapshotSchemaVersion`

并在架构文档中说明未来 schema 升级和历史快照迁移原则。

## Phase 1 验收

- Snapshot 可完整 Serialize / Deserialize；
- Round-trip 后核心结构一致；
- 测试通过；
- 不依赖 Microsoft Word；
- macOS CI 不受影响。

完成后更新本文：

`Phase 1: DONE`

---

# 7. Phase 2 — 文本、Style 与 Effective Formatting

## 文本解析

不要仅依赖 `InnerText`。

至少正确处理常见：

- `w:t`
- `w:tab`
- `w:br`
- `w:cr`

特殊结构不能导致整段解析失败。

---

## 字符格式

至少支持：

- Font family
- Font size
- Color
- Bold
- Italic
- Underline
- Strike
- Highlight

字体模型应保留 Word 中：

- ascii
- hAnsi
- eastAsia
- cs

不能永久压缩为单个 FontFamily。

---

## 段落格式

至少支持：

- Alignment
- Left / Right indent
- First line indent
- Hanging indent
- Space before
- Space after
- Line spacing
- Line rule

---

## Style

至少解析：

- Paragraph Style
- Character Style
- 基础 Table Style
- BasedOn
- Default Style
- Linked Style（适用时）

---

## Effective Formatting

建立独立 resolver，例如：

`EffectiveFormattingResolver`

需要处理常见级联：

```text
Document Defaults
→ Style inheritance
→ Paragraph Style
→ Run Style
→ Direct Paragraph Formatting
→ Direct Run Formatting
```

必须处理：

- Style 缺失；
- BasedOn 循环；
- 非法引用。

不能无限递归。

异常进入 Diagnostics。

## Phase 2 验收

测试至少覆盖：

- 中文字体；
- 英文字体；
- 字号；
- 颜色；
- Bold；
- Underline；
- Paragraph Alignment；
- Indent；
- Spacing；
- Style inheritance；
- Direct vs Effective Formatting。

完成后：

`Phase 2: DONE`

---

# 8. Phase 3 — Revision 与 Comment

## Revision

正式支持：

- Insert
- Delete

至少保存：

- 类型；
- Revision ID；
- Author；
- Date；
- 所属位置；
- 文本内容。

必须正确读取删除文本相关结构，例如：

`w:delText`

不得通过 Accept All Revisions 简化处理。

---

## Formatting Revision

研究并建立正式模型或 Diagnostics 支持：

- Run property change
- Paragraph property change
- 可合理支持时加入 Table property change

暂不完整支持的类型必须明确记录。

不得静默丢弃。

---

## Comment

至少保存：

- Comment ID
- Author
- Initials
- Date
- Content
- Anchor start
- Anchor end
- 所属节点
- 是否成功关联正文

需要处理：

- CommentRangeStart
- CommentRangeEnd
- CommentReference

无法可靠建立 Anchor 时：

保留 Comment，并产生 Diagnostic。

## Phase 3 验收

测试至少覆盖：

- Insert；
- Delete；
- Author；
- Date；
- Comment content；
- Comment author；
- Comment anchor；
- Revision + Comment 同时存在。

完成后：

`Phase 3: DONE`

---

# 9. Phase 4 — Numbering、Table、Protection 与其他结构

## Numbering

至少读取：

- numId
- ilvl
- abstractNumId
- number format
- level text
- start value

不得把 Word 自动编号信息丢失。

本阶段不做完整合同条款识别。

---

## Table

不能只保存为二维文本数组。

至少保存：

- Table / Row / Cell NodeId；
- 行列位置；
- Cell paragraphs；
- Width；
- Alignment；
- Shading；
- Borders 基础信息；
- Grid Span；
- Vertical Merge。

表格 Cell 必须允许多个 Paragraph。

嵌套 Table 即使暂不完整支持，也不得造成解析器崩溃。

---

## Section

至少读取：

- 顺序；
- 页面大小；
- Orientation；
- Margins。

精确页码不是本阶段任务。

---

## Header / Footer

至少：

- 发现相关 Part；
- 记录存在；
- 尽量读取基础文本。

---

## Hyperlink

至少保证：

- 显示文本不会丢失。

如实现成本合理，同时保存 External Target。

---

## Protection

重点验证：

### 限制编辑 DOCX

如果 DOCX 只是 Word 限制编辑，但 Open XML 仍可读取：

Final Check 必须继续解析：

- 正文；
- Revision；
- Comment；
- Formatting；
- Table。

保存基础 Protection Metadata。

### 真正加密文件

如果 Package 无法打开：

返回明确错误类型。

禁止尝试破解密码。

必须区分：

- Restricted Editing
- Encrypted
- Corrupted / Invalid Package

## Phase 4 验收

测试至少覆盖：

- Numbering；
- Table；
- Merged Cell；
- Section；
- Header/Footer；
- Hyperlink；
- Restricted Editing；
- 无法正常读取的文件错误分类。

完成后：

`Phase 4: DONE`

---

# 10. Phase 5 — Diagnostics、Persistence、Performance

## ParseDiagnostics

建立正式模型。

至少支持：

- Info
- Warning
- Error

Diagnostic 至少包含：

- Code
- Message
- Part / Node
- WhetherContentWasSkipped

示例 Code 可包括：

- BrokenStyleReference
- UnresolvedCommentAnchor
- UnsupportedElement
- InvalidNumberingReference
- EncryptedPackage

---

## Parse Status

Snapshot 至少具有：

- Complete
- Partial
- Failed

一个局部结构不支持时：

优先 Partial，而不是整份文档 Failed。

---

## Snapshot Persistence

验证完整流程：

```text
DocumentSnapshot
→ Serialize
→ SQLite
→ Read
→ Deserialize
→ Compare core structure
```

不要把每一个 Run 拆成大量 EF Entity。

优先使用 Snapshot Payload。

---

## Compression Spike

比较：

- JSON
- GZip 或 Brotli

至少记录：

- 原始大小；
- 压缩大小；
- Serialization time；
- Deserialization time。

如果正式采用压缩方案：

同步更新 Architecture。

---

## Cancellation

主要解析流程需要支持：

`CancellationToken`

至少在主要段落 / 表格循环中响应取消。

---

## Progress

预留可向上层报告的解析阶段：

- Opening
- Reading styles
- Reading document
- Reading tables
- Reading comments
- Building snapshot
- Completed

不要求精确百分比。

---

# 11. 测试 Fixture

不得使用用户真实合同。

建立小型、明确、可重复的测试 Fixtures。

至少覆盖：

- PlainText
- MultipleRuns
- CharacterFormatting
- ParagraphFormatting
- Styles
- StyleInheritance
- Numbering
- Table
- MergedCells
- RevisionInsert
- RevisionDelete
- Comment
- RevisionAndComment
- DocumentProtection
- HeaderFooter
- Hyperlink
- MixedChineseEnglishFont

优先程序化生成。

只有不适合程序化生成的极小 Word 结构才保存固定 DOCX fixture。

不得使用大型模拟合同验证单个解析功能。

---

# 12. Golden Snapshot

对于少量核心 Fixture 可以建立 Golden Snapshot。

要求：

- 小型；
- 人类可读；
- 无随机时间；
- 无随机 ID；
- Diff 易于审查。

不得把整个测试体系变成大量难维护的 Snapshot 文件。

---

# 13. 内存原则

必须保持：

```text
Open DOCX
→ Parse
→ Build Snapshot
→ Dispose WordprocessingDocument
```

解析完成后不能长期保留 Open XML Package。

禁止：

- 全局大型 Snapshot Cache；
- 静态缓存整个 Document；
- 打开一个项目就加载全部历史 Snapshot。

必要 resolver cache 必须限定在单文档解析生命周期。

---

# 14. 性能基线

继续维护：

`docs/development/performance-baseline.md`

需要修正初始化阶段 Working Set 测量方法。

当前历史值：

- Initial Working Set: 182.27 MiB
- 20s Working Set: 1.11 MiB
- Private Memory: 109.63 MiB

其中 1.11 MiB Working Set 明显需要重新验证。

新测量至少记录：

- Process ID
- 测量时 App 状态
- Working Set
- Private Memory
- Parse time
- Managed allocations
- Snapshot JSON size
- Compressed size

禁止调用 `EmptyWorkingSet` 等手段人为降低指标。

---

# 15. 跨平台约束

必须保持：

- Windows CI 通过；
- macOS CI 通过。

`FinalCheck.Documents` 不得增加：

- COM
- Office Interop
- Word.exe
- Registry
- Windows-only API

Final Check 官方仍以 Windows 为主要支持平台，但 Document Engine 必须保持跨平台。

---

# 16. 文档

本阶段至少新增：

`docs/architecture/document-engine.md`

内容至少包括：

- Parser pipeline
- Snapshot model
- Node identity
- Styles
- Effective formatting
- Revisions
- Comments
- Numbering
- Tables
- Protection
- Diagnostics
- Persistence
- Schema version
- Known limitations

同步更新：

- `docs/development/README.md`
- `docs/development/performance-baseline.md`

如产生重大架构决策，再创建 ADR。

---

# 17. 每个 Phase 的执行纪律

每完成一个 Phase：

1. 运行该阶段相关测试；
2. 确认无已有能力回归；
3. 更新本任务文件状态；
4. 简要记录实际完成内容；
5. 再进入下一 Phase。

不要等五个 Phase 全做完后才第一次运行测试。

---

# 18. 当前进度

由 Codex 在执行过程中维护：

- Phase 1 — Snapshot Model：DONE
  - 实际完成：schema v2、Document/Section/Paragraph/Run/Table/Row/Cell 领域结构、确定性节点身份、父子与来源位置、SHA-256 元数据、JSON round-trip、v1 显式迁移及 Package 生命周期均已实现。
  - 验证：2026-09-10 运行 Documents Phase 1 聚焦测试 5 项及 Core 测试 2 项，全部通过。
- Phase 2 — Effective Formatting：DONE
  - 实际完成：逐元素解析文本/Tab/Break/CR，保留四类字体槽与字符、段落格式；实现 document defaults、默认/显式段落样式、BasedOn、字符样式、段落标记和 direct formatting 的有效格式级联，并对缺失、类型错误和循环引用诊断。
  - 验证：2026-09-10 运行 Phase 2 聚焦测试 7 项，全部通过。
- Phase 3 — Revision / Comment：DONE
  - 实际完成：支持 Insert/Delete 与 run/paragraph/table property change，保存修订元数据、位置、文本和可读取的旧格式；保存 Comment 内容、作者、initials、时间及范围/引用锚点，无法关联时保留并诊断。
  - 验证：2026-09-10 运行 Phase 3 聚焦测试 4 项，全部通过。
- Phase 4 — Structure / Protection：DONE
  - 实际完成：保存 numbering 定义/实例/段落引用，结构化 Table/Row/Cell 与合并、边框、底色、宽度、对齐，读取 Section、Header/Footer、Hyperlink 和 restricted-editing metadata；加密、非 DOCX、损坏包及缺失文件使用明确错误类型。
  - 验证：2026-09-10 运行 Phase 4 聚焦测试 8 项，全部通过；嵌套表格以 Partial + Diagnostic 降级而不崩溃。
- Phase 5 — Persistence / Performance：DONE
  - 实际完成：正式 Diagnostic/ParseStatus、SQLite payload round-trip、取消与阶段进度已实现；JSON/GZip/Brotli Spike 与同 PID 桌面内存复测已记录，正式存储仍使用可迁移的 UTF-8 JSON。
  - 验证：2026-09-10 运行 persistence 测试 1 项、diagnostics/cancellation/progress 测试 4 项及 Release 性能测试 1 项，全部通过；综合 fixture 解析 110.502 ms、managed allocations 4,127,784 B、JSON 1,016,466 B、GZip 48,731 B、Brotli 27,379 B。

- 最终本地验收：2026-09-10 `dotnet restore`、Release build（0 warning / 0 error）及 Release tests（31/31）全部通过；Release 桌面 App 已启动并确认主窗口存在、进程响应正常。

如果某 Phase 部分完成：

使用：

`IN PROGRESS`

并简要记录剩余内容。

---

# 19. 最终验收

最终至少执行：

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

并验证：

- App 仍可启动；
- Windows CI 通过；
- macOS CI 通过；
- Working tree 符合预期；
- 没有新增 Windows-only 核心依赖。

---

# 20. Git

全部验收通过后：

建议 Commit：

`feat: build document engine snapshot foundation`

正常 push。

禁止：

- force push
- reset --hard
- 重写历史
- 创建 v0.1.0 Tag
- 创建 Release

---

# 21. 完成报告

最终报告至少包含：

## Document Engine

- Snapshot schema
- Node identity
- Parsing coverage
- Effective formatting
- Revision
- Comment
- Numbering
- Table
- Protection
- Diagnostics
- Persistence

## Tests

- Total
- Passed
- Failed
- Fixtures

## Performance

- Parse time
- Managed allocations
- Working Set
- Private Memory
- Snapshot JSON size
- Compressed size

## Platform

- Windows
- macOS CI
- Windows-only dependency check

## Documentation

- document-engine.md
- Development docs
- Performance baseline
- ADR（如有）

## Git

- Commit
- Push
- Working tree

## Known Limitations

明确列出仍未支持和仅部分支持的 Open XML 结构。

最后判断：

> Document Engine v0 是否已经具备进入 Comparison Engine v0 阶段的稳定基础。
