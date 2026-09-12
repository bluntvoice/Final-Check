using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Comparison;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Formatting;

namespace FinalCheck.Documents.Tests;

internal static class FormatRestoreFixtureFactory
{
    public static MemoryStream Create(Body body, Styles? styles = null, Comments? comments = null, bool trackChanges = false)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(body);
            if (styles is not null) main.AddNewPart<StyleDefinitionsPart>().Styles = styles;
            if (comments is not null) main.AddNewPart<WordprocessingCommentsPart>().Comments = comments;
            if (trackChanges) main.AddNewPart<DocumentSettingsPart>().Settings = new Settings(new TrackRevisions());
            main.Document.Save();
        }
        stream.Position = 0;
        return stream;
    }

    public static RunProperties CharacterProperties() => new(
        new RunFonts { Ascii = "Arial", HighAnsi = "Calibri", EastAsia = "宋体", ComplexScript = "Times New Roman" },
        new Bold(), new Italic(), new Color { Val = "336699" }, new FontSize { Val = "24" },
        new Underline { Val = UnderlineValues.Single }, new Strike(), new Highlight { Val = HighlightColorValues.Yellow });

    public static BasicComparisonEngine Engine()
    {
        var diff = new TokenTextDiffService(new MixedLanguageTextTokenizer());
        var format = new EffectiveFormatDiffService();
        return new(new MultiSignalParagraphMatcher(), diff, new ParagraphMoveDetector(), format,
            new TableComparisonService(diff, format), new SnapshotAnnotationIntegrationService(), new RuleBasedChangeGroupingService());
    }

    // Tests of property writing explicitly trust the fixture's known paragraph correspondence.
    public static FormatRestorePlan Plan(DocumentSnapshot baseline, DocumentSnapshot current)
    {
        var comparison = Engine().Compare(baseline, current);
        if (baseline.Paragraphs.Count > 0 && baseline.Paragraphs.Count == current.Paragraphs.Count)
            comparison = comparison with { NodeMappings = comparison.NodeMappings.Where(m => m.CurrentNode.Kind != DocumentNodeKind.Paragraph).Concat(baseline.Paragraphs.Zip(current.Paragraphs).Select(pair =>
                new ComparisonNodeMapping(new(pair.First.NodeId, DocumentNodeKind.Paragraph, pair.First.Index),
                    new(pair.Second.NodeId, DocumentNodeKind.Paragraph, pair.Second.Index), ComparisonMappingType.ExactText,
                    ComparisonConfidenceLevel.High, 1, [], false, pair.First.DisplayText != pair.Second.DisplayText))).ToArray() };
        return new SnapshotFormatRestorePlanner().Generate(baseline, current, comparison);
    }
}
