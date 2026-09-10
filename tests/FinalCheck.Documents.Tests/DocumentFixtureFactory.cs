using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace FinalCheck.Documents.Tests;

internal static class DocumentFixtureFactory
{
    private static readonly DateTime FixtureDateUtc = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    public static MemoryStream PlainText() => Create((_, body) =>
        body.Append(new Paragraph(new Run(new Text("普通合同文本")))));

    public static MemoryStream MultipleRuns() => Create((_, body) =>
        body.Append(new Paragraph(
            new Run(new Text("第一段")),
            new Run(new Text("第二个 Run")))));

    public static MemoryStream TabAndBreak() => Create((_, body) =>
        body.Append(new Paragraph(new Run(
            new Text("A"),
            new TabChar(),
            new Text("B"),
            new Break(),
            new Text("C"),
            new CarriageReturn(),
            new Text("D")))));

    public static MemoryStream CharacterFormatting() => Create((_, body) =>
        body.Append(new Paragraph(new Run(
            new RunProperties(
                new RunFonts { Ascii = "Arial", HighAnsi = "Arial", EastAsia = "宋体", ComplexScript = "Arial" },
                new FontSize { Val = "24" },
                new Color { Val = "336699" },
                new Bold(),
                new Italic(),
                new Underline { Val = UnderlineValues.Single },
                new Strike(),
                new Highlight { Val = HighlightColorValues.Yellow }),
            new Text("格式文本")))));

    public static MemoryStream ParagraphFormatting() => Create((_, body) =>
        body.Append(new Paragraph(
            new ParagraphProperties(
                new Justification { Val = JustificationValues.Center },
                new Indentation { Left = "240", Right = "120", FirstLine = "480", Hanging = "60" },
                new SpacingBetweenLines { Before = "100", After = "200", Line = "360", LineRule = LineSpacingRuleValues.Auto }),
            new Run(new Text("段落格式")))));

    public static MemoryStream Styles() => Create((mainPart, body) =>
    {
        AddStyles(mainPart, includeInheritance: false);
        body.Append(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "ContractBody" }),
            new Run(new Text("样式正文"))));
    });

    public static MemoryStream StyleInheritance() => Create((mainPart, body) =>
    {
        AddStyles(mainPart, includeInheritance: true);
        body.Append(new Paragraph(
            new ParagraphProperties(
                new ParagraphStyleId { Val = "ChildBody" },
                new SpacingBetweenLines { After = "240" }),
            new Run(
                new RunProperties(
                    new RunStyle { Val = "Emphasis" },
                    new Color { Val = "AA0000" }),
                new Text("继承样式"))));
    });

    public static MemoryStream BrokenStyleReference() => Create((mainPart, body) =>
    {
        AddStyles(mainPart, includeInheritance: false);
        body.Append(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "MissingStyle" }),
            new Run(new Text("缺失样式引用"))));
    });

    public static MemoryStream Numbering() => Create((mainPart, body) =>
    {
        var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
        numberingPart.Numbering = new Numbering(
            new AbstractNum(
                new Level(
                    new StartNumberingValue { Val = 1 },
                    new NumberingFormat { Val = NumberFormatValues.Decimal },
                    new LevelText { Val = "%1." })
                { LevelIndex = 0 })
            { AbstractNumberId = 3 },
            new NumberingInstance(new AbstractNumId { Val = 3 }) { NumberID = 7 });
        body.Append(new Paragraph(
            new ParagraphProperties(new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = 7 })),
            new Run(new Text("自动编号条款"))));
    });

    public static MemoryStream Table() => Create((_, body) =>
        body.Append(new Table(
            new TableProperties(
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Dxa },
                new TableJustification { Val = TableRowAlignmentValues.Center },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                    new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "000000" })),
            new TableRow(
                Cell("A1", "D9EAF7"),
                Cell("B1", "FFFFFF")),
            new TableRow(
                Cell("A2", "FFFFFF"),
                Cell("B2", "FFFFFF")))));

    public static MemoryStream MergedCells() => Create((_, body) =>
    {
        var cell = new TableCell(
            new TableCellProperties(
                new GridSpan { Val = 2 },
                new VerticalMerge { Val = MergedCellValues.Restart },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
            new Paragraph(new Run(new Text("合并单元格"))));
        body.Append(new Table(new TableRow(cell)));
    });

    public static MemoryStream RevisionInsert() => Create((_, body) =>
        body.Append(new Paragraph(new InsertedRun(
            new Run(new Text("插入内容")))
        {
            Id = "1",
            Author = "Fixture Author",
            Date = FixtureDateUtc,
        })));

    public static MemoryStream RevisionDelete() => Create((_, body) =>
        body.Append(new Paragraph(new DeletedRun(
            new Run(new DeletedText("删除内容")))
        {
            Id = "2",
            Author = "Fixture Author",
            Date = FixtureDateUtc,
        })));

    public static MemoryStream Comment() => Create((mainPart, body) =>
    {
        body.Append(CommentedParagraph("批注锚点", "0"));
        AddCommentPart(mainPart, "0", "测试批注");
    });

    public static MemoryStream UnanchoredComment() => Create((mainPart, body) =>
    {
        body.Append(new Paragraph(new Run(new Text("没有批注范围的正文"))));
        AddCommentPart(mainPart, "9", "未关联批注");
    });

    public static MemoryStream RevisionAndComment() => Create((mainPart, body) =>
    {
        body.Append(new Paragraph(
            new InsertedRun(new Run(new Text("插入内容")))
            {
                Id = "1",
                Author = "Fixture Author",
                Date = FixtureDateUtc,
            },
            new DeletedRun(new Run(new DeletedText("删除内容")))
            {
                Id = "2",
                Author = "Fixture Author",
                Date = FixtureDateUtc,
            },
            new CommentRangeStart { Id = "0" },
            new Run(new Text("带批注文本")),
            new CommentRangeEnd { Id = "0" },
            new Run(new CommentReference { Id = "0" })));
        AddCommentPart(mainPart, "0", "修订与批注");
    });

    public static MemoryStream FormatRevisions() => Create((_, body) =>
    {
        var paragraphChange = new ParagraphPropertiesChange(
            new ParagraphPropertiesExtended(new Justification { Val = JustificationValues.Right }))
        {
            Id = "3",
            Author = "Fixture Author",
            Date = FixtureDateUtc,
        };
        var runChange = new RunPropertiesChange(new PreviousRunProperties(new Bold()))
        {
            Id = "4",
            Author = "Fixture Author",
            Date = FixtureDateUtc,
        };
        var tableChange = new TablePropertiesChange(
            new PreviousTableProperties(new TableWidth { Width = "4000", Type = TableWidthUnitValues.Dxa }))
        {
            Id = "5",
            Author = "Fixture Author",
            Date = FixtureDateUtc,
        };
        var paragraph = new Paragraph(
            new ParagraphProperties(paragraphChange),
            new Run(new RunProperties(runChange), new Text("格式修订")));
        var table = new Table(
            new TableProperties(tableChange),
            new TableRow(Cell("表格格式修订", "FFFFFF")));
        body.Append(paragraph, table);
    });

    public static MemoryStream StyleCycle() => Create((mainPart, body) =>
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new Style(new BasedOn { Val = "CycleB" }) { Type = StyleValues.Paragraph, StyleId = "CycleA" },
            new Style(new BasedOn { Val = "CycleA" }) { Type = StyleValues.Paragraph, StyleId = "CycleB" });
        body.Append(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "CycleA" }),
            new Run(new Text("循环样式"))));
    });

    public static MemoryStream DocumentProtection() => Create((mainPart, body) =>
    {
        var settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings = new Settings(new DocumentProtection
        {
            Edit = DocumentProtectionValues.ReadOnly,
            Enforcement = true,
        });
        body.Append(new Paragraph(new Run(new Text("受限制但未加密的正文"))));
        body.Append(new Table(new TableRow(Cell("仍可读取", "FFFFFF"))));
    });

    public static MemoryStream HeaderFooter() => Create((mainPart, body) =>
    {
        var headerPart = mainPart.AddNewPart<HeaderPart>();
        headerPart.Header = new Header(new Paragraph(new Run(new Text("页眉文本"))));
        var footerPart = mainPart.AddNewPart<FooterPart>();
        footerPart.Footer = new Footer(new Paragraph(new Run(new Text("页脚文本"))));
        body.Append(
            new Paragraph(new Run(new Text("正文"))),
            new SectionProperties(
                new HeaderReference { Id = mainPart.GetIdOfPart(headerPart), Type = HeaderFooterValues.Default },
                new FooterReference { Id = mainPart.GetIdOfPart(footerPart), Type = HeaderFooterValues.Default },
                new PageSize { Width = 11906, Height = 16838, Orient = PageOrientationValues.Portrait },
                new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440 }));
    });

    public static MemoryStream Hyperlink() => Create((mainPart, body) =>
    {
        var relationship = mainPart.AddHyperlinkRelationship(new Uri("https://example.test/contract"), true);
        body.Append(new Paragraph(new Hyperlink(
            new Run(new Text("合同链接")))
        { Id = relationship.Id }));
    });

    public static MemoryStream MixedChineseEnglishFont() => Create((_, body) =>
        body.Append(new Paragraph(new Run(
            new RunProperties(new RunFonts
            {
                Ascii = "Arial",
                HighAnsi = "Calibri",
                EastAsia = "宋体",
                ComplexScript = "Times New Roman",
            }),
            new Text("中文 Contract 123")))));

    public static MemoryStream UnsupportedNestedTable() => Create((_, body) =>
        body.Append(new Table(new TableRow(new TableCell(
            new Paragraph(new Run(new Text("外层"))),
            new Table(new TableRow(Cell("内层", "FFFFFF"))))))));

    public static MemoryStream PerformanceComposite(int paragraphCount = 250, int tableCount = 12) => Create((mainPart, body) =>
    {
        AddStyles(mainPart, includeInheritance: true);
        for (var index = 0; index < paragraphCount; index++)
        {
            body.Append(new Paragraph(
                new ParagraphProperties(
                    new ParagraphStyleId { Val = index % 5 == 0 ? "ChildBody" : "ContractBody" },
                    new SpacingBetweenLines { After = "120", Line = "360", LineRule = LineSpacingRuleValues.Auto }),
                new Run(new RunProperties(new RunFonts { Ascii = "Arial", EastAsia = "宋体" }), new Text($"第{index + 1}条 ")),
                new Run(new Text("本测试条款用于 Document Engine 综合性能基线，包含中英文 Contract 内容。"))));
        }

        for (var tableIndex = 0; tableIndex < tableCount; tableIndex++)
        {
            body.Append(new Table(
                new TableRow(Cell($"表{tableIndex + 1}-A", "D9EAF7"), Cell("B", "FFFFFF")),
                new TableRow(Cell("C", "FFFFFF"), Cell("D", "FFFFFF"))));
        }
    });

    private static MemoryStream Create(Action<MainDocumentPart, Body> populate)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            populate(mainPart, body);
            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    private static void AddStyles(MainDocumentPart mainPart, bool includeInheritance)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", EastAsia = "等线" },
                    new FontSize { Val = "22" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new Justification { Val = JustificationValues.Left }))),
            new Style(
                new StyleName { Val = "Contract Body" },
                new StyleRunProperties(
                    new RunFonts { Ascii = "Arial", HighAnsi = "Arial", EastAsia = "宋体" },
                    new FontSize { Val = "24" }),
                new StyleParagraphProperties(
                    new Indentation { Left = "240", FirstLine = "480" },
                    new SpacingBetweenLines { After = "120" }))
            {
                Type = StyleValues.Paragraph,
                StyleId = "ContractBody",
                Default = true,
            },
            new Style(
                new StyleName { Val = "Emphasis" },
                new StyleRunProperties(new Italic(), new Underline { Val = UnderlineValues.Single }))
            {
                Type = StyleValues.Character,
                StyleId = "Emphasis",
            });
        if (includeInheritance)
        {
            styles.Append(new Style(
                new StyleName { Val = "Child Body" },
                new BasedOn { Val = "ContractBody" },
                new LinkedStyle { Val = "Emphasis" },
                new StyleRunProperties(new Bold()),
                new StyleParagraphProperties(new Justification { Val = JustificationValues.Center }))
            {
                Type = StyleValues.Paragraph,
                StyleId = "ChildBody",
            });
        }

        stylesPart.Styles = styles;
    }

    private static TableCell Cell(string text, string fill) => new(
        new TableCellProperties(
            new TableCellWidth { Width = "2400", Type = TableWidthUnitValues.Dxa },
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center },
            new Shading { Fill = fill },
            new GridSpan { Val = 1 },
            new TableCellBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "000000" })),
        new Paragraph(new Run(new Text(text))));

    private static Paragraph CommentedParagraph(string text, string commentId) => new(
        new CommentRangeStart { Id = commentId },
        new Run(new Text(text)),
        new CommentRangeEnd { Id = commentId },
        new Run(new CommentReference { Id = commentId }));

    private static void AddCommentPart(MainDocumentPart mainPart, string commentId, string text)
    {
        var commentsPart = mainPart.AddNewPart<WordprocessingCommentsPart>();
        commentsPart.Comments = new Comments(
            new Comment(new Paragraph(new Run(new Text(text))))
            {
                Id = commentId,
                Author = "Fixture Author",
                Initials = "FA",
                Date = FixtureDateUtc,
            });
    }
}
