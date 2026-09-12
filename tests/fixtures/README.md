# Document Engine generated fixtures

Document Engine fixtures are generated in memory by
`tests/FinalCheck.Documents.Tests/DocumentFixtureFactory.cs`. No real contract
or user document is stored in this directory.

Each generated fixture is intentionally small and isolates one WordprocessingML
capability. Add a new fixture method and focused assertion when a parser
regression is found. Only commit a binary `.docx` here when the Open XML SDK
cannot construct the minimal structure and document the reason beside it.

Current fixture catalog:

- PlainText
- MultipleRuns
- TabAndBreak
- CharacterFormatting
- ParagraphFormatting
- Styles
- StyleInheritance
- BrokenStyleReference
- Numbering
- Table
- MergedCells
- RevisionInsert
- RevisionDelete
- Comment
- UnanchoredComment
- RevisionAndComment
- FormatRevisions
- StyleCycle
- DocumentProtection
- HeaderFooter
- Hyperlink
- MixedChineseEnglishFont
- UnsupportedNestedTable
- PerformanceComposite

## Format Restore generated fixtures

`FormatRestoreFixtureFactory`, `FormatRestoreCharacterTests`, `FormatRestoreParagraphTests`,
`FormatRestoreTableTests` and `FormatRestoreSafetyTests` generate fixtures in memory.
`FormatRestoreWorkflowTests` writes only isolated GUID temporary DOCX/SQLite files.
No real contract or user database is used. Semantic assertions cover:

- CharacterFontRestore, FontSizeRestore, MultipleCharacterProperties, ChineseEnglishFonts
- ParagraphAlignment, ParagraphIndent, ParagraphSpacing, LineSpacing
- StyleBasedFormatting, DirectFormatting, ChangedTextSameNode, ChangedRunBoundary
- AddedText, AddedParagraph, RevisionInsertPreservation, RevisionDeletePreservation
- CommentPreservation, ExistingFormatRevision (all five property Change kinds), TrackChangesEnabled
- TableFormatting, CellFormatting, TableStructureChanged, merge structure preservation
- UndoLastRestore, ExternalWorkingCopyModification, explicit preserve/regenerate
- IdempotentRestore, SelectedItems/Category scope, deterministic serializable plan
- Cancellation before/during write, operation lock conflict, database failure rollback
- Prepared-journal crash recovery, schema migration/history preservation, unknown payload rejection
- Small (10 paragraphs) and Medium (300 paragraphs) performance measurements

Property-writing tests may explicitly declare known fixture correspondence; workflow tests
use the real Comparison Engine. Successful outputs are checked for text/format/annotations/XML
structure, not expected DOCX binary equality; original files and failed working writes are
also checked byte-for-byte.
