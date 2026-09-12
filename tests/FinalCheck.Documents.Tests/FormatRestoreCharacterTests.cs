using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Core.Formatting;

namespace FinalCheck.Documents.Tests;

public sealed class FormatRestoreCharacterTests
{
    private readonly OpenXmlDocumentParser _parser = new();

    [Fact]
    public async Task RestoresAllCharacterPropertiesWithoutRestoringOldText()
    {
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(FormatRestoreFixtureFactory.CharacterProperties(), new Text("付款期限为30日")))));
        using var current = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(new Text("付款期限为60日新增文字")))));
        var original = current.ToArray();
        var b = await _parser.ParseAsync(baseline);
        var c = await _parser.ParseAsync(current);
        var plan = FormatRestoreFixtureFactory.Plan(b, c);
        var result = await new OpenXmlFormatRestoreRenderer(_parser).RenderAsync(current, plan, new(FormatRestoreScopeKind.Category, FormatRestoreCategory.Character));
        Assert.Equal("付款期限为60日新增文字", result.Snapshot.Paragraphs[0].DisplayText);
        Assert.Equal(b.Paragraphs[0].Runs[0].EffectiveFormatting, result.Snapshot.Paragraphs[0].Runs[0].EffectiveFormatting);
        Assert.Equal(original, current.ToArray());
        Assert.Single(result.Mutations);
        Assert.Contains(plan.Diagnostics, d => d.Code == "AddedTextFallbackUsed");
    }

    [Fact]
    public async Task DifferentRunBoundariesRestoreWithoutRebuildingRuns()
    {
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(FormatRestoreFixtureFactory.CharacterProperties(), new Text("付款期限为30日")))));
        using var current = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(new Text("付款期限为")), new Run(new Text("60日")))));
        var b = await _parser.ParseAsync(baseline);
        var c = await _parser.ParseAsync(current);
        var result = await new OpenXmlFormatRestoreRenderer(_parser).RenderAsync(current, FormatRestoreFixtureFactory.Plan(b, c));
        Assert.Equal(2, result.Snapshot.Paragraphs[0].Runs.Count);
        Assert.All(result.Snapshot.Paragraphs[0].Runs, r => Assert.Equal(b.Paragraphs[0].Runs[0].EffectiveFormatting, r.EffectiveFormatting));
    }

    [Fact]
    public async Task MixedFormatsUseTextPositionAndSkipConflictingRun()
    {
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(
            new Run(new RunProperties(new Bold()), new Text("甲方")), new Run(new RunProperties(new Italic()), new Text("付款")))));
        using var current = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(new Text("甲方")), new Run(new Text("付款")))));
        var b = await _parser.ParseAsync(baseline);
        var c = await _parser.ParseAsync(current);
        var result = await new OpenXmlFormatRestoreRenderer(_parser).RenderAsync(current, FormatRestoreFixtureFactory.Plan(b, c));
        Assert.True(result.Snapshot.Paragraphs[0].Runs[0].EffectiveFormatting.Bold);
        Assert.True(result.Snapshot.Paragraphs[0].Runs[1].EffectiveFormatting.Italic);
        using var conflicting = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(new Text("甲方付款")))));
        var plan = FormatRestoreFixtureFactory.Plan(b, await _parser.ParseAsync(conflicting));
        Assert.Empty(plan.RestoreItems);
        Assert.Contains(plan.Diagnostics, d => d.Code == "MixedRunFormattingNeedsReview");
    }

    [Fact]
    public async Task RevisionsCommentsAndExistingFormatRevisionArePreserved()
    {
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(new RunProperties(new Bold()), new Text("正文插入")))));
        var oldProperties = new RunPropertiesChange(new PreviousRunProperties(new Italic())) { Id = "3", Author = "Fixture" };
        using var current = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(
            new CommentRangeStart { Id = "0" }, new Run(new RunProperties(oldProperties), new Text("正文")),
            new InsertedRun(new Run(new Text("插入"))) { Id = "1", Author = "Fixture" },
            new DeletedRun(new Run(new DeletedText("删除"))) { Id = "2", Author = "Fixture" },
            new CommentRangeEnd { Id = "0" }, new Run(new CommentReference { Id = "0" }))),
            comments: new Comments(new Comment(new Paragraph(new Run(new Text("批注")))) { Id = "0", Author = "Fixture" }), trackChanges: true);
        var b = await _parser.ParseAsync(baseline);
        var c = await _parser.ParseAsync(current);
        var result = await new OpenXmlFormatRestoreRenderer(_parser).RenderAsync(current, FormatRestoreFixtureFactory.Plan(b, c));
        Assert.Equal(JsonSerializer.Serialize(c.Revisions), JsonSerializer.Serialize(result.Snapshot.Revisions));
        Assert.Equal(JsonSerializer.Serialize(c.Comments), JsonSerializer.Serialize(result.Snapshot.Comments));
        Assert.Equal(c.Paragraphs[0].RawText, result.Snapshot.Paragraphs[0].RawText);
        using var package = WordprocessingDocument.Open(new MemoryStream(result.DocumentBytes), false);
        Assert.Single(package.MainDocumentPart!.Document!.Descendants<RunPropertiesChange>());
    }

    [Fact]
    public async Task CompatibleInheritedStyleClearsOverrideWithoutFlattening()
    {
        Styles Styles() => new(new Style(new StyleRunProperties(new Bold())) { StyleId = "Body", Type = StyleValues.Paragraph });
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Body" }), new Run(new Text("合同")))), Styles());
        using var current = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Body" }), new Run(new RunProperties(new Bold { Val = false }), new Text("合同")))), Styles());
        var result = await new OpenXmlFormatRestoreRenderer(_parser).RenderAsync(current, FormatRestoreFixtureFactory.Plan(await _parser.ParseAsync(baseline), await _parser.ParseAsync(current)));
        Assert.Null(result.Snapshot.Paragraphs[0].Runs[0].DirectFormatting.Bold);
        Assert.True(result.Snapshot.Paragraphs[0].Runs[0].EffectiveFormatting.Bold);
        Assert.Equal("Body", result.Snapshot.Paragraphs[0].StyleId);
    }

    [Fact]
    public async Task SelectedItemsCancellationAndStaleHashAreSafe()
    {
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(new RunProperties(new Bold()), new Text("合同")))));
        using var current = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(new Text("合同")))));
        var plan = FormatRestoreFixtureFactory.Plan(await _parser.ParseAsync(baseline), await _parser.ParseAsync(current));
        var renderer = new OpenXmlFormatRestoreRenderer(_parser);
        var empty = await renderer.RenderAsync(current, plan, new(FormatRestoreScopeKind.SelectedItems, SelectedItemIds: []));
        Assert.Empty(empty.Mutations);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await renderer.RenderAsync(current, plan, cancellationToken: new(true)));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await renderer.RenderAsync(current, plan with { SourceSha256 = new string('a', 64) }));
    }
}
