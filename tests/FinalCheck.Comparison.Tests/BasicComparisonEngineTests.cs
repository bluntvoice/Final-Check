using FinalCheck.Comparison;
using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison.Tests;

public sealed class BasicComparisonEngineTests
{
    [Fact]
    public void CompareClassifiesModifiedAndAddedParagraphs()
    {
        var source = CreateSnapshot("付款期限30日");
        var target = CreateSnapshot("付款期限60日", "新增条款");
        var engine = new BasicComparisonEngine(
            new PositionalStructureMatcher(),
            new WholeTextDiffService());

        var result = engine.Compare(source, target);

        Assert.Collection(
            result.Changes,
            change =>
            {
                Assert.Equal(ComparisonChangeKind.Modified, change.Kind);
                Assert.Equal("付款期限30日", change.Difference.OldText);
                Assert.Equal("付款期限60日", change.Difference.NewText);
            },
            change => Assert.Equal(ComparisonChangeKind.Added, change.Kind));
    }

    private static DocumentSnapshot CreateSnapshot(params string[] text) => DocumentSnapshot.Empty with
    {
        Paragraphs = text.Select((value, index) => new DocumentParagraphSnapshot(
            new DocumentNodeIdentitySnapshot(
                $"body/p[{index}]",
                null,
                DocumentNodeKind.Paragraph,
                $"body/p[{index}]",
                "/word/document.xml",
                index),
            index,
            value,
            value,
            string.IsNullOrEmpty(value),
            null,
            [],
            ParagraphFormatSnapshot.Empty,
            ParagraphFormatSnapshot.Empty,
            null)).ToArray(),
    };
}
