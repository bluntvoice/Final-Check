# Document Engine v0

## 目标与边界

Document Engine v0 将可读取的 DOCX 转换为稳定、可序列化的 `DocumentSnapshot`。它不执行版本比对、格式恢复、条款智能识别或业务 UI；这些能力只能消费 Snapshot，不能持有 Open XML SDK 对象或继续依赖已关闭的 Package。

## Parser pipeline

```text
Validate input and package signature
→ Open WordprocessingDocument read-only
→ Read styles and document defaults
→ Read numbering
→ Read body paragraphs, runs and tables
→ Read revisions, comments, sections, headers/footers, hyperlinks and protection
→ Build diagnostics and parse status
→ Build DocumentSnapshot
→ Dispose WordprocessingDocument
```

`IDocumentParser` 支持文件与可定位流，并在主要阶段报告 `DocumentParseProgress`。取消令牌在打开前、正文、表格、行、单元格、修订和批注循环中检查。Package 与 resolver cache 都限定在单次解析生命周期内。

## Snapshot model

当前 `SnapshotSchemaVersion` 为 2。Snapshot 包含文件元数据与 SHA-256、Section、Paragraph/Run、Table/Row/Cell、Style、Numbering、Revision、Comment、Hyperlink、Protection、Header/Footer、ParseStatus 和 Diagnostics。文本同时保留原始值与显示值，删除修订文字不会混入显示文本。

Snapshot 只包含领域记录、标量与集合，不保存 `OpenXmlElement`、Part 或 Package 引用。

## Node identity

Section、Paragraph、Run、Table、Row 和 Cell 使用确定性的结构路径作为 `NodeId`，并保存：

- `ParentNodeId`
- `DocumentNodeKind`
- `StructuralPath`
- `SourcePart`
- `SourceIndex`

同一字节内容在相同 schema/parser 版本下产生相同结构身份。身份只保证 Snapshot 内唯一和同结构重解析稳定；文档编辑导致结构位置变化时，不承诺跨版本不变。后续 `DocumentNodeMapping` 负责跨文档对应。

## 文本与格式

Run 内容逐元素处理 `w:t`、`w:delText`、`w:tab`、`w:br`、`w:cr` 和 field code，不以 `InnerText` 作为正文解析捷径。字符格式保留 ascii、hAnsi、eastAsia、cs 及对应 theme 字体槽，以及字号、颜色、粗体、斜体、下划线、删除线和高亮。段落格式保存对齐、缩进、段前后距、行距和 line rule。

## Styles 与 Effective formatting

`OpenXmlFormattingResolver` 在单文档范围内建立 style 索引。常见级联顺序为：

```text
Document defaults
→ BasedOn ancestors
→ default/explicit paragraph style
→ character style
→ paragraph mark direct formatting
→ direct run formatting
```

段落直接格式在样式后覆盖。字符样式中的 on/off 属性按 Word toggle 语义叠加，直接格式则按显式值覆盖。缺失引用、类型错误和 `BasedOn` 循环不会无限递归，而是停止该链并产生 Diagnostic。Linked style 作为元数据保存，v0 不自动把 linked style 转换成另一类型样式。

## Revisions 与 Comments

Revision 支持插入、删除，以及 run/paragraph/table property change。保存 revision ID、作者、UTC 时间、文本、受影响节点和可读取的旧格式。删除文字从 `w:delText` 读取；不会接受或改写修订。

Comment 保存 ID、作者、initials、UTC 时间、内容、范围起止、引用 Run 与所属 Paragraph。无法关联的 Comment 仍保留，并产生 `UnresolvedCommentAnchor`。

## Numbering

保存 abstract numbering、numbering instance，以及段落的 numId/ilvl、abstractNumId、number format、level text 和 start value。缺失或无法解析的引用产生 `InvalidNumberingReference`。v0 不计算最终展示编号，也不执行条款识别。

## Tables 与其他结构

Table、Row、Cell 都有独立身份。Cell 保存行列坐标、多个 Paragraph、宽度、垂直对齐、底色、边框、grid span 和 vertical merge；Table 保存样式、宽度、对齐、底色和基础边框。嵌套 Table 当前不展开，外层内容仍解析，并以 `UnsupportedNestedTable` 标记 Partial。

Section 保存页面大小、方向和页边距。Header/Footer 保存 part、relationship 与基础文本。Hyperlink 保留显示文本，并在可解析时保存外部目标。

## Protection 与错误分类

`w:documentProtection` 代表限制编辑：只记录元数据，不阻止读取正文、修订、批注、格式和表格。OLE compound envelope 被分类为 `Encrypted`，不会尝试破解。无效 ZIP/OPC 包分类为 `UnsupportedFormat`、`InvalidPackage` 或 `CorruptedPackage`，文件缺失分类为 `FileNotFound`。

## Diagnostics 与 ParseStatus

Diagnostic 包含 severity、code、message、node、part 和 `ContentWasSkipped`。只有 Info 时 Snapshot 为 `Complete`；存在 Warning/Error 时为 `Partial`。无法建立 Snapshot 的包级失败通过带 `DocumentParseErrorKind` 的 `DocumentParseException` 返回，调用方可据此表现为 Failed；v0 不构造没有正文事实的伪 Snapshot。

## Serialization 与 persistence

`JsonDocumentSnapshotSerializer` 只写当前 schema；读取时拒绝未来 schema，并可把 v1 已知内容迁移为 v2 Partial Snapshot。SQLite 通过一个 payload 保存完整 Snapshot，同时单独保存 schema version 和创建时间，不把每个 Run 展开为 EF Entity。

当前正式 payload 使用未压缩 JSON。GZip/Brotli 只做性能 Spike；是否持久化压缩格式需以后以独立 schema/存储迁移决策采用，不能静默改变已有 payload。

## Schema version 与迁移原则

- 任何破坏 JSON 兼容性或解释语义的变更都必须提升 `SnapshotSchemaVersion`。
- serializer 只生成当前版本；历史版本通过显式、可测试、逐版本迁移读取。
- 未知未来版本必须拒绝读取，不能猜测字段语义。
- 无法从旧 schema 恢复的结构应保留已知内容、标记 Partial 并产生 Diagnostic。
- 数据库 payload 与 schema version 必须一致；迁移失败不得覆盖原 payload。

该决策记录于 [`ADR-0002-document-snapshot-schema-and-persistence.md`](ADR-0002-document-snapshot-schema-and-persistence.md)。

## Known limitations

- 不计算 Word 布局、精确页码、主题解析后的实际字体或最终渲染结果。
- 不展开嵌套表格；复杂 drawing、content control、altChunk、公式、脚注/尾注等仅诊断或尚未建模。
- 不计算自动编号最终字符串、列表重启的完整继承和全部 level override。
- 格式修订保存常见旧属性，但未覆盖 WordprocessingML 的全部 revision 类型和条件表格样式。
- Comment 仅建立基础范围/引用关系，不解析线程回复、现代批注扩展或跨复杂结构的精确字符区间。
- 加密文件只分类，不提供密码解密。
- Snapshot 是结构化历史事实，不保证能还原完整 DOCX。
