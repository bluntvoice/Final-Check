using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Comparison;
using FinalCheck.Core.Formatting;

namespace FinalCheck.Documents.Tests;

public sealed class FormatRestoreParagraphTests
{
    private readonly OpenXmlDocumentParser _parser = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoresAlignmentIndentSpacingAndLineRuleWithoutChangingText(bool hanging)
    {
        var indentation = new Indentation { Left = "240", Right = "120" };
        if (hanging) indentation.Hanging = "360"; else indentation.FirstLine = "480";
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new ParagraphProperties(
            new Justification { Val = JustificationValues.Center }, indentation,
            new SpacingBetweenLines { Before = "100", After = "200", Line = "360", LineRule = LineSpacingRuleValues.Auto }), new Run(new Text("原文")))));
        using var current = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(new Text("修改后正文")))));
        var b = await _parser.ParseAsync(baseline);
        var c = await _parser.ParseAsync(current);
        var result = await new OpenXmlFormatRestoreRenderer(_parser).RenderAsync(current, FormatRestoreFixtureFactory.Plan(b, c), new(FormatRestoreScopeKind.Category, FormatRestoreCategory.Paragraph));
        Assert.Equal("修改后正文", result.Snapshot.Paragraphs[0].DisplayText);
        Assert.Equal(b.Paragraphs[0].EffectiveFormatting, result.Snapshot.Paragraphs[0].EffectiveFormatting);
    }

    [Fact]
    public async Task CompatibleStyleReferenceRestoresWithoutFlattening()
    {
        Styles Styles() => new(new Style(new StyleParagraphProperties(new Justification { Val = JustificationValues.Center })) { StyleId = "Template", Type = StyleValues.Paragraph },
            new Style { StyleId = "Current", Type = StyleValues.Paragraph });
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Template" }), new Run(new Text("合同")))), Styles());
        using var current = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Current" }), new Run(new Text("合同")))), Styles());
        var result = await new OpenXmlFormatRestoreRenderer(_parser).RenderAsync(current, FormatRestoreFixtureFactory.Plan(await _parser.ParseAsync(baseline), await _parser.ParseAsync(current)));
        Assert.Equal("Template", result.Snapshot.Paragraphs[0].StyleId);
        Assert.Null(result.Snapshot.Paragraphs[0].DirectFormatting.Alignment);
        Assert.Equal("center", result.Snapshot.Paragraphs[0].EffectiveFormatting.Alignment);
    }

    [Fact]
    public async Task MissingStyleUsesMinimalOverrideAndDiagnostic()
    {
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Template" }), new Run(new Text("合同")))),
            new Styles(new Style(new StyleParagraphProperties(new Justification { Val = JustificationValues.Center })) { StyleId = "Template", Type = StyleValues.Paragraph }));
        using var current = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(new Text("合同")))));
        var result = await new OpenXmlFormatRestoreRenderer(_parser).RenderAsync(current, FormatRestoreFixtureFactory.Plan(await _parser.ParseAsync(baseline), await _parser.ParseAsync(current)));
        Assert.Null(result.Snapshot.Paragraphs[0].StyleId);
        Assert.Equal("center", result.Snapshot.Paragraphs[0].EffectiveFormatting.Alignment);
        Assert.Contains(result.Diagnostics, d => d.Code == "StyleReferencePreserved");
    }

    [Fact]
    public async Task AddedParagraphUsesSameLevelTrustedNeighbourConsensus()
    {
        Paragraph P(string text, bool formatted) => new(formatted ? new ParagraphProperties(new Justification { Val = JustificationValues.Center }) : new ParagraphProperties(), new Run(new Text(text)));
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(P("第一条已有内容", true), P("第二条已有内容", true)));
        using var current = FormatRestoreFixtureFactory.Create(new Body(P("第一条已有内容", false), P("新增条款", false), P("第二条已有内容", false)));
        var b = await _parser.ParseAsync(baseline);
        var c = await _parser.ParseAsync(current);
        var plan = new SnapshotFormatRestorePlanner().Generate(b, c, FormatRestoreFixtureFactory.Engine().Compare(b, c));
        Assert.Contains(plan.Diagnostics, d => d.Code == "AddedParagraphFallbackUsed");
        var result = await new OpenXmlFormatRestoreRenderer(_parser).RenderAsync(current, plan);
        Assert.Equal("新增条款", result.Snapshot.Paragraphs[1].DisplayText);
        Assert.Equal("center", result.Snapshot.Paragraphs[1].EffectiveFormatting.Alignment);
    }

    [Fact]
    public async Task AddedParagraphWithoutConsensusIsNotAutomaticallyRestored()
    {
        using var baseline = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(new Text("第一条")))));
        using var current = FormatRestoreFixtureFactory.Create(new Body(new Paragraph(new Run(new Text("第一条"))), new Paragraph(new Run(new Text("新增")))));
        var b = await _parser.ParseAsync(baseline);
        var c = await _parser.ParseAsync(current);
        var plan = new SnapshotFormatRestorePlanner().Generate(b, c, FormatRestoreFixtureFactory.Engine().Compare(b, c));
        Assert.DoesNotContain(plan.RestoreItems, i => i.CurrentNodeId.StartsWith("body/p[1]", StringComparison.Ordinal));
        Assert.Contains(plan.Diagnostics, d => d.Code == "UnmappedNode");
    }
}
