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
