using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison.Tests;

internal static class ComparisonFixtureFactory
{
    public static DocumentSnapshot FromText(params string[] paragraphs) => Create(
        paragraphs.Select(text => new ParagraphSpec(text)).ToArray());

    public static DocumentSnapshot Create(params ParagraphSpec[] paragraphs) => DocumentSnapshot.Empty with
    {
        Paragraphs = paragraphs.Select(CreateParagraph).ToArray(),
    };

    private static DocumentParagraphSnapshot CreateParagraph(ParagraphSpec spec, int index)
    {
        var paragraphId = $"body/p[{index}]";
        var runTexts = spec.Runs is { Count: > 0 } ? spec.Runs : [spec.Text];
        var runs = runTexts.Select((text, runIndex) => new DocumentRunSnapshot(
            new DocumentNodeIdentitySnapshot(
                $"{paragraphId}/r[{runIndex}]",
                paragraphId,
                DocumentNodeKind.Run,
                $"{paragraphId}/r[{runIndex}]",
                "/word/document.xml",
                runIndex),
            runIndex,
            paragraphId,
            text,
            text,
            string.IsNullOrEmpty(text),
            [new RunContentSnapshot(RunContentKind.Text, text, text, 0)],
            spec.CharacterFormatting ?? CharacterFormatSnapshot.Empty,
            spec.CharacterFormatting ?? CharacterFormatSnapshot.Empty)).ToArray();
        return new DocumentParagraphSnapshot(
            new DocumentNodeIdentitySnapshot(
                paragraphId,
                null,
                DocumentNodeKind.Paragraph,
                paragraphId,
                "/word/document.xml",
                index),
            index,
            spec.Text,
            spec.Text,
            string.IsNullOrEmpty(spec.Text),
            spec.StyleId,
            runs,
            spec.ParagraphFormatting ?? ParagraphFormatSnapshot.Empty,
            spec.ParagraphFormatting ?? ParagraphFormatSnapshot.Empty,
            spec.Numbering);
    }

    internal sealed record ParagraphSpec(
        string Text,
        string? StyleId = null,
        ParagraphNumberingReferenceSnapshot? Numbering = null,
        ParagraphFormatSnapshot? ParagraphFormatting = null,
        CharacterFormatSnapshot? CharacterFormatting = null,
        IReadOnlyList<string>? Runs = null);
}
