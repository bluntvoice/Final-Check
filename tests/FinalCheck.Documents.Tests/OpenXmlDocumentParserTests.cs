using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using FinalCheck.Core.Documents;
using FinalCheck.Documents;
using Xunit.Abstractions;

namespace FinalCheck.Documents.Tests;

public sealed class OpenXmlDocumentParserTests(ITestOutputHelper output)
{
    private readonly OpenXmlDocumentParser _parser = new();

    [Fact]
    public async Task ParseBuildsStableParagraphAndRunStructure()
    {
        await using var stream = DocumentFixtureFactory.MultipleRuns();

        var snapshot = await _parser.ParseAsync(stream);

        var paragraph = Assert.Single(snapshot.Paragraphs);
        Assert.Equal("第一段第二个 Run", paragraph.DisplayText);
        Assert.Equal(2, paragraph.Runs.Count);
        Assert.Equal("body/p[0]", paragraph.Identity.StructuralPath);
        Assert.All(paragraph.Runs, run => Assert.Equal(paragraph.NodeId, run.Identity.ParentNodeId));
        Assert.Equal(snapshot.Paragraphs.SelectMany(item => item.Runs).Count() + 1,
            snapshot.Paragraphs.SelectMany(item => item.Runs).Select(item => item.NodeId).Append(paragraph.NodeId).Distinct().Count());
    }

    [Fact]
    public async Task StableIdentitiesAndContentHashAreDeterministicAcrossParses()
    {
        await using var source = DocumentFixtureFactory.MultipleRuns();
        var payload = source.ToArray();
        await using var firstStream = new MemoryStream(payload);
        await using var secondStream = new MemoryStream(payload);

        var first = await _parser.ParseAsync(firstStream);
        var second = await _parser.ParseAsync(secondStream);

        Assert.Equal(first.Metadata.Sha256, second.Metadata.Sha256);
        Assert.Equal(64, first.Metadata.Sha256.Length);
        Assert.Equal(
            first.Paragraphs.SelectMany(paragraph => paragraph.Runs).Select(run => run.NodeId),
            second.Paragraphs.SelectMany(paragraph => paragraph.Runs).Select(run => run.NodeId));
    }

    [Fact]
    public async Task ParsePreservesTabBreakAndCarriageReturnSemantics()
    {
        await using var stream = DocumentFixtureFactory.TabAndBreak();

        var run = Assert.Single(Assert.Single((await _parser.ParseAsync(stream)).Paragraphs).Runs);

        Assert.Equal("A\tB\nC\nD", run.DisplayText);
        Assert.Equal(
            [RunContentKind.Text, RunContentKind.Tab, RunContentKind.Text, RunContentKind.Break,
                RunContentKind.Text, RunContentKind.CarriageReturn, RunContentKind.Text],
            run.Content.Select(item => item.Kind));
    }

    [Fact]
    public async Task ParseReadsDirectCharacterFormattingAndSeparateFontSlots()
    {
        await using var stream = DocumentFixtureFactory.CharacterFormatting();

        var format = Assert.Single(Assert.Single((await _parser.ParseAsync(stream)).Paragraphs).Runs).DirectFormatting;

        Assert.Equal("Arial", format.Fonts.Ascii);
        Assert.Equal("宋体", format.Fonts.EastAsia);
        Assert.Equal(24, format.FontSizeHalfPoints);
        Assert.Equal("336699", format.Color);
        Assert.True(format.Bold);
        Assert.True(format.Italic);
        Assert.Equal("single", format.Underline);
        Assert.True(format.Strike);
        Assert.Equal("yellow", format.Highlight);
    }

    [Fact]
    public async Task ParsePreservesMixedChineseAndEnglishFontSlots()
    {
        await using var stream = DocumentFixtureFactory.MixedChineseEnglishFont();

        var fonts = Assert.Single(Assert.Single((await _parser.ParseAsync(stream)).Paragraphs).Runs).DirectFormatting.Fonts;

        Assert.Equal("Arial", fonts.Ascii);
        Assert.Equal("Calibri", fonts.HighAnsi);
        Assert.Equal("宋体", fonts.EastAsia);
        Assert.Equal("Times New Roman", fonts.ComplexScript);
    }

    [Fact]
    public async Task ParseReadsParagraphFormatting()
    {
        await using var stream = DocumentFixtureFactory.ParagraphFormatting();

        var format = Assert.Single((await _parser.ParseAsync(stream)).Paragraphs).DirectFormatting;

        Assert.Equal("center", format.Alignment);
        Assert.Equal("240", format.LeftIndent);
        Assert.Equal("120", format.RightIndent);
        Assert.Equal("480", format.FirstLineIndent);
        Assert.Equal("60", format.HangingIndent);
        Assert.Equal("100", format.SpacingBefore);
        Assert.Equal("200", format.SpacingAfter);
        Assert.Equal("360", format.LineSpacing);
        Assert.Equal("auto", format.LineRule);
    }

    [Fact]
    public async Task ResolveEffectiveFormattingFollowsStyleInheritance()
    {
        await using var stream = DocumentFixtureFactory.StyleInheritance();

        var snapshot = await _parser.ParseAsync(stream);
        var paragraph = Assert.Single(snapshot.Paragraphs);
        var run = Assert.Single(paragraph.Runs);

        Assert.Equal("center", paragraph.EffectiveFormatting.Alignment);
        Assert.Equal("240", paragraph.EffectiveFormatting.LeftIndent);
        Assert.Equal("240", paragraph.DirectFormatting.SpacingAfter);
        Assert.Equal("240", paragraph.EffectiveFormatting.SpacingAfter);
        Assert.Equal("Arial", run.EffectiveFormatting.Fonts.Ascii);
        Assert.Equal("宋体", run.EffectiveFormatting.Fonts.EastAsia);
        Assert.Equal(24, run.EffectiveFormatting.FontSizeHalfPoints);
        Assert.True(run.EffectiveFormatting.Bold);
        Assert.True(run.EffectiveFormatting.Italic);
        Assert.Equal("single", run.EffectiveFormatting.Underline);
        Assert.Equal("AA0000", run.DirectFormatting.Color);
        Assert.Equal("AA0000", run.EffectiveFormatting.Color);
        Assert.Contains(snapshot.Styles, style => style.StyleId == "ChildBody" && style.BasedOnStyleId == "ContractBody");
    }

    [Fact]
    public async Task BrokenStyleReferenceProducesDiagnosticAndPartialSnapshot()
    {
        await using var stream = DocumentFixtureFactory.BrokenStyleReference();

        var snapshot = await _parser.ParseAsync(stream);

        Assert.Equal(DocumentParseStatus.Partial, snapshot.ParseStatus);
        Assert.Contains(snapshot.ParseDiagnostics, diagnostic => diagnostic.Code == "BrokenStyleReference");
    }

    [Fact]
    public async Task StyleCycleProducesDiagnosticWithoutRecursionFailure()
    {
        await using var stream = DocumentFixtureFactory.StyleCycle();

        var snapshot = await _parser.ParseAsync(stream);

        Assert.Contains(snapshot.ParseDiagnostics, diagnostic => diagnostic.Code == "StyleInheritanceCycle");
        Assert.Equal(DocumentParseStatus.Partial, snapshot.ParseStatus);
    }

    [Fact]
    public async Task ParseReadsNumberingDefinitionAndParagraphReference()
    {
        await using var stream = DocumentFixtureFactory.Numbering();

        var snapshot = await _parser.ParseAsync(stream);
        var reference = Assert.Single(snapshot.Paragraphs).Numbering;

        Assert.NotNull(reference);
        Assert.Equal(7, reference.NumberingId);
        Assert.Equal(0, reference.LevelIndex);
        Assert.Equal(3, reference.AbstractNumberingId);
        Assert.Equal("decimal", reference.NumberFormat);
        Assert.Equal("%1.", reference.LevelText);
        Assert.Equal(1, reference.StartValue);
    }

    [Fact]
    public async Task ParseBuildsTableRowsCellsParagraphsAndFormatting()
    {
        await using var stream = DocumentFixtureFactory.Table();

        var table = Assert.Single((await _parser.ParseAsync(stream)).Tables);

        Assert.Equal(2, table.Rows.Count);
        Assert.All(table.Rows, row => Assert.Equal(2, row.Cells.Count));
        var firstCell = table.Rows[0].Cells[0];
        Assert.Equal("A1", firstCell.DisplayText);
        Assert.Single(firstCell.Paragraphs);
        Assert.Equal("D9EAF7", firstCell.DirectFormatting.ShadingFill);
        Assert.Equal("center", firstCell.DirectFormatting.VerticalAlignment);
        Assert.Equal("single", firstCell.DirectFormatting.Borders.Top?.Style);
    }

    [Fact]
    public async Task ParseReadsMergedCellState()
    {
        await using var stream = DocumentFixtureFactory.MergedCells();

        var cell = Assert.Single(Assert.Single(Assert.Single((await _parser.ParseAsync(stream)).Tables).Rows).Cells);

        Assert.Equal(2, cell.DirectFormatting.GridSpan);
        Assert.Equal("restart", cell.DirectFormatting.VerticalMerge);
    }

    [Fact]
    public async Task ParseReadsInsertDeleteAndFormatRevisionMetadata()
    {
        await using var revisionStream = DocumentFixtureFactory.RevisionAndComment();
        var snapshot = await _parser.ParseAsync(revisionStream);
        await using var formatStream = DocumentFixtureFactory.FormatRevisions();
        var formatSnapshot = await _parser.ParseAsync(formatStream);

        var insert = Assert.Single(snapshot.Revisions, revision => revision.Kind == DocumentRevisionKind.Insert);
        var delete = Assert.Single(snapshot.Revisions, revision => revision.Kind == DocumentRevisionKind.Delete);
        Assert.Equal("插入内容", insert.Text);
        Assert.Equal("删除内容", delete.Text);
        Assert.Equal("Fixture Author", insert.Author);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), insert.TimestampUtc);
        Assert.NotNull(insert.ParagraphNodeId);
        Assert.Contains(formatSnapshot.Revisions, revision => revision.Kind == DocumentRevisionKind.RunPropertyChange && revision.IsSupported);
        Assert.Contains(formatSnapshot.Revisions, revision => revision.Kind == DocumentRevisionKind.ParagraphPropertyChange && revision.IsSupported);
        Assert.True(formatSnapshot.Revisions.Single(revision => revision.Kind == DocumentRevisionKind.RunPropertyChange).PreviousCharacterFormatting?.Bold);
        Assert.Equal("right", formatSnapshot.Revisions.Single(revision => revision.Kind == DocumentRevisionKind.ParagraphPropertyChange).PreviousParagraphFormatting?.Alignment);
        Assert.Equal("4000", formatSnapshot.Revisions.Single(revision => revision.Kind == DocumentRevisionKind.TablePropertyChange).PreviousTableFormatting?.Width);
    }

    [Fact]
    public async Task DeletedRevisionTextRemainsRawButIsExcludedFromDisplayText()
    {
        await using var stream = DocumentFixtureFactory.RevisionDelete();

        var paragraph = Assert.Single((await _parser.ParseAsync(stream)).Paragraphs);

        Assert.Equal("删除内容", paragraph.RawText);
        Assert.Equal(string.Empty, paragraph.DisplayText);
        Assert.Equal(RunContentKind.DeletedText, Assert.Single(Assert.Single(paragraph.Runs).Content).Kind);
    }

    [Fact]
    public async Task ParseAssociatesCommentContentAuthorAndAnchors()
    {
        await using var stream = DocumentFixtureFactory.Comment();

        var comment = Assert.Single((await _parser.ParseAsync(stream)).Comments);

        Assert.Equal("测试批注", comment.Text);
        Assert.Equal("Fixture Author", comment.Author);
        Assert.Equal("FA", comment.Initials);
        Assert.True(comment.IsAnchored);
        Assert.NotNull(comment.AnchorStartNodeId);
        Assert.NotNull(comment.AnchorEndNodeId);
        Assert.NotNull(comment.ParagraphNodeId);
    }

    [Fact]
    public async Task UnanchoredCommentIsPreservedWithDiagnostic()
    {
        await using var stream = DocumentFixtureFactory.UnanchoredComment();

        var snapshot = await _parser.ParseAsync(stream);

        var comment = Assert.Single(snapshot.Comments);
        Assert.Equal("未关联批注", comment.Text);
        Assert.False(comment.IsAnchored);
        Assert.Equal(DocumentParseStatus.Partial, snapshot.ParseStatus);
        Assert.Contains(snapshot.ParseDiagnostics, diagnostic =>
            diagnostic.Code == "UnresolvedCommentAnchor" && !diagnostic.ContentWasSkipped);
    }

    [Fact]
    public async Task RestrictedEditingDoesNotPreventContentParsing()
    {
        await using var stream = DocumentFixtureFactory.DocumentProtection();

        var snapshot = await _parser.ParseAsync(stream);

        Assert.True(snapshot.Protection.IsPresent);
        Assert.True(snapshot.Protection.Enforcement);
        Assert.Equal("readOnly", snapshot.Protection.EditMode);
        Assert.Equal("受限制但未加密的正文", Assert.Single(snapshot.Paragraphs).DisplayText);
        Assert.Single(snapshot.Tables);
    }

    [Fact]
    public async Task ParseReadsSectionHeaderFooterAndHyperlink()
    {
        await using var sectionStream = DocumentFixtureFactory.HeaderFooter();
        var sectionSnapshot = await _parser.ParseAsync(sectionStream);
        await using var hyperlinkStream = DocumentFixtureFactory.Hyperlink();
        var hyperlinkSnapshot = await _parser.ParseAsync(hyperlinkStream);

        var section = Assert.Single(sectionSnapshot.Sections);
        Assert.Equal((uint)11906, section.PageWidthTwips);
        Assert.Equal("portrait", section.Orientation);
        Assert.Contains(sectionSnapshot.HeaderFooters, item => item.Kind == "Header" && item.Text == "页眉文本");
        Assert.Contains(sectionSnapshot.HeaderFooters, item => item.Kind == "Footer" && item.Text == "页脚文本");
        Assert.Equal("合同链接", Assert.Single(hyperlinkSnapshot.Paragraphs).DisplayText);
        Assert.Equal("https://example.test/contract", Assert.Single(hyperlinkSnapshot.Hyperlinks).Target);
    }

    [Fact]
    public async Task UnsupportedNestedTableProducesPartialSnapshotWithoutCrash()
    {
        await using var stream = DocumentFixtureFactory.UnsupportedNestedTable();

        var snapshot = await _parser.ParseAsync(stream);

        Assert.Equal(DocumentParseStatus.Partial, snapshot.ParseStatus);
        Assert.Contains(snapshot.ParseDiagnostics, diagnostic =>
            diagnostic.Code == "UnsupportedNestedTable" && diagnostic.ContentWasSkipped);
    }

    [Fact]
    public async Task ParseReportsProgressAndHonorsCancellation()
    {
        await using var stream = DocumentFixtureFactory.PlainText();
        var progress = new RecordingProgress();

        _ = await _parser.ParseAsync(stream, progress);

        Assert.Equal(DocumentParseStage.Opening, progress.Stages[0]);
        Assert.Equal(DocumentParseStage.Completed, progress.Stages[^1]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var cancelledStream = DocumentFixtureFactory.PlainText();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _parser.ParseAsync(cancelledStream, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task ParseClassifiesEncryptedUnsupportedAndCorruptPackages()
    {
        await using var encrypted = new MemoryStream([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]);
        var encryptedError = await Assert.ThrowsAsync<DocumentParseException>(async () => await _parser.ParseAsync(encrypted));
        await using var unsupported = new MemoryStream(Encoding.UTF8.GetBytes("not a docx"));
        var unsupportedError = await Assert.ThrowsAsync<DocumentParseException>(async () => await _parser.ParseAsync(unsupported));
        await using var corrupt = new MemoryStream([0x50, 0x4B, 0x03, 0x04, 0x01, 0x02, 0x03]);
        var corruptError = await Assert.ThrowsAsync<DocumentParseException>(async () => await _parser.ParseAsync(corrupt));

        Assert.Equal(DocumentParseErrorKind.Encrypted, encryptedError.ErrorKind);
        Assert.Equal(DocumentParseErrorKind.UnsupportedFormat, unsupportedError.ErrorKind);
        Assert.Equal(DocumentParseErrorKind.CorruptedPackage, corruptError.ErrorKind);
    }

    [Fact]
    public async Task ParseFileClassifiesMissingDocx()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");

        var error = await Assert.ThrowsAsync<DocumentParseException>(async () =>
            await _parser.ParseFileAsync(missingPath));

        Assert.Equal(DocumentParseErrorKind.FileNotFound, error.ErrorKind);
    }

    [Fact]
    public async Task SnapshotSerializationRoundTripPreservesCoreStructures()
    {
        await using var stream = DocumentFixtureFactory.RevisionAndComment();
        var revisionSnapshot = await _parser.ParseAsync(stream);
        await using var stylesStream = DocumentFixtureFactory.StyleInheritance();
        var stylesSnapshot = await _parser.ParseAsync(stylesStream);
        await using var tableStream = DocumentFixtureFactory.Table();
        var tableSnapshot = await _parser.ParseAsync(tableStream);
        await using var numberingStream = DocumentFixtureFactory.Numbering();
        var numberingSnapshot = await _parser.ParseAsync(numberingStream);
        var snapshot = revisionSnapshot with
        {
            Styles = stylesSnapshot.Styles,
            Defaults = stylesSnapshot.Defaults,
            Tables = tableSnapshot.Tables,
            Numbering = numberingSnapshot.Numbering,
            ParseStatus = DocumentParseStatus.Partial,
            ParseDiagnostics = [new DocumentParseDiagnostic(
                DocumentDiagnosticSeverity.Warning,
                "GoldenDiagnostic",
                "Golden round-trip diagnostic.",
                revisionSnapshot.Paragraphs[0].NodeId,
                "/word/document.xml",
                false)],
        };
        var serializer = new JsonDocumentSnapshotSerializer();

        var payload = serializer.Serialize(snapshot);
        var roundTrip = serializer.Deserialize(payload);

        Assert.Equal(DocumentSnapshot.CurrentSchemaVersion, roundTrip.SnapshotSchemaVersion);
        Assert.Equal(snapshot.Paragraphs[0].Identity, roundTrip.Paragraphs[0].Identity);
        Assert.Equal(snapshot.Revisions[0], roundTrip.Revisions[0]);
        Assert.Equal(snapshot.Comments[0], roundTrip.Comments[0]);
        Assert.Equal(snapshot.Styles[0], roundTrip.Styles[0]);
        Assert.Equal(snapshot.Tables[0].Rows[0].Cells[0].DisplayText, roundTrip.Tables[0].Rows[0].Cells[0].DisplayText);
        Assert.Equal(snapshot.Numbering.Instances[0], roundTrip.Numbering.Instances[0]);
        Assert.Equal(snapshot.ParseDiagnostics[0], roundTrip.ParseDiagnostics[0]);
        Assert.Equal(payload, serializer.Serialize(roundTrip));
    }

    [Fact]
    public void SnapshotSerializerMigratesSchemaV1WithoutDiscardingKnownContent()
    {
        const string legacyJson = """
            {"snapshotSchemaVersion":1,"paragraphs":[{"nodeId":"paragraph:0","index":0,"text":"旧快照","runs":[],"format":{"styleId":null,"alignment":null,"leftIndent":null,"firstLineIndent":null}}],"tables":[],"revisions":[],"comments":[]}
            """;

        var snapshot = new JsonDocumentSnapshotSerializer().Deserialize(Encoding.UTF8.GetBytes(legacyJson));

        Assert.Equal(DocumentSnapshot.CurrentSchemaVersion, snapshot.SnapshotSchemaVersion);
        Assert.Equal("旧快照", Assert.Single(snapshot.Paragraphs).DisplayText);
        Assert.Equal(DocumentParseStatus.Partial, snapshot.ParseStatus);
        Assert.Contains(snapshot.ParseDiagnostics, diagnostic => diagnostic.Code == "LegacySnapshotMigrated");
    }

    [Fact]
    public async Task ParseDisposesPackageAndLeavesSourceStreamUsable()
    {
        await using var stream = DocumentFixtureFactory.PlainText();

        _ = await _parser.ParseAsync(stream);

        Assert.True(stream.CanRead);
        stream.Position = 0;
        Assert.NotEqual(-1, stream.ReadByte());
    }

    [Fact]
    public async Task PerformanceCompositeRecordsJsonAndCompressionBaseline()
    {
        await using var stream = DocumentFixtureFactory.PerformanceComposite();
        var serializer = new JsonDocumentSnapshotSerializer();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var workingSetBefore = process.WorkingSet64;
        var privateBefore = process.PrivateMemorySize64;
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var parseWatch = Stopwatch.StartNew();

        var snapshot = await _parser.ParseAsync(stream);

        parseWatch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        var serializeWatch = Stopwatch.StartNew();
        var json = serializer.Serialize(snapshot);
        serializeWatch.Stop();
        var gzipWatch = Stopwatch.StartNew();
        var gzip = Compress(json, CompressionKind.GZip);
        gzipWatch.Stop();
        var brotliWatch = Stopwatch.StartNew();
        var brotli = Compress(json, CompressionKind.Brotli);
        brotliWatch.Stop();
        var deserializeWatch = Stopwatch.StartNew();
        _ = serializer.Deserialize(json);
        deserializeWatch.Stop();
        process.Refresh();

        output.WriteLine("PERF_FIXTURE=PerformanceComposite(250 paragraphs, 12 tables, 48 cells)");
        output.WriteLine("PERF_PROCESS_ID={0}", process.Id);
        output.WriteLine("PERF_PARSE_MS={0:F3}", parseWatch.Elapsed.TotalMilliseconds);
        output.WriteLine("PERF_MANAGED_ALLOCATED_BYTES={0}", allocated);
        output.WriteLine("PERF_WORKING_SET_BEFORE_BYTES={0}", workingSetBefore);
        output.WriteLine("PERF_WORKING_SET_AFTER_BYTES={0}", process.WorkingSet64);
        output.WriteLine("PERF_PRIVATE_MEMORY_BEFORE_BYTES={0}", privateBefore);
        output.WriteLine("PERF_PRIVATE_MEMORY_AFTER_BYTES={0}", process.PrivateMemorySize64);
        output.WriteLine("PERF_JSON_BYTES={0}", json.Length);
        output.WriteLine("PERF_GZIP_BYTES={0}", gzip.Length);
        output.WriteLine("PERF_BROTLI_BYTES={0}", brotli.Length);
        output.WriteLine("PERF_SERIALIZE_MS={0:F3}", serializeWatch.Elapsed.TotalMilliseconds);
        output.WriteLine("PERF_DESERIALIZE_MS={0:F3}", deserializeWatch.Elapsed.TotalMilliseconds);
        output.WriteLine("PERF_GZIP_MS={0:F3}", gzipWatch.Elapsed.TotalMilliseconds);
        output.WriteLine("PERF_BROTLI_MS={0:F3}", brotliWatch.Elapsed.TotalMilliseconds);

        Assert.Equal(250, snapshot.Paragraphs.Count);
        Assert.Equal(12, snapshot.Tables.Count);
        Assert.True(gzip.Length < json.Length);
        Assert.True(brotli.Length < json.Length);
    }

    private static byte[] Compress(byte[] payload, CompressionKind kind)
    {
        using var destination = new MemoryStream();
        using (Stream compressor = kind == CompressionKind.GZip
            ? new GZipStream(destination, CompressionLevel.Fastest, true)
            : new BrotliStream(destination, CompressionLevel.Fastest, true))
        {
            compressor.Write(payload);
        }

        return destination.ToArray();
    }

    private sealed class RecordingProgress : IProgress<DocumentParseProgress>
    {
        public List<DocumentParseStage> Stages { get; } = [];

        public void Report(DocumentParseProgress value) => Stages.Add(value.Stage);
    }

    private enum CompressionKind
    {
        GZip,
        Brotli,
    }
}
