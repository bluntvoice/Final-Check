using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison;

public sealed class PositionalStructureMatcher : IStructureMatcher
{
    public IReadOnlyList<(DocumentParagraphSnapshot? Source, DocumentParagraphSnapshot? Target)> Match(
        DocumentSnapshot source,
        DocumentSnapshot target)
    {
        var count = Math.Max(source.Paragraphs.Count, target.Paragraphs.Count);
        var matches = new List<(DocumentParagraphSnapshot?, DocumentParagraphSnapshot?)>(count);
        for (var index = 0; index < count; index++)
        {
            matches.Add((
                index < source.Paragraphs.Count ? source.Paragraphs[index] : null,
                index < target.Paragraphs.Count ? target.Paragraphs[index] : null));
        }

        return matches;
    }
}

public sealed class WholeTextDiffService : ITextDiffService
{
    public TextDifference Compare(string oldText, string newText) => new(oldText, newText);
}

public sealed class BasicComparisonEngine(
    IStructureMatcher structureMatcher,
    ITextDiffService textDiffService) : IComparisonEngine
{
    public ComparisonResult Compare(DocumentSnapshot source, DocumentSnapshot target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var changes = new List<ComparisonChange>();
        foreach (var (sourceParagraph, targetParagraph) in structureMatcher.Match(source, target))
        {
            if (sourceParagraph is null && targetParagraph is not null)
            {
                changes.Add(new ComparisonChange(
                    ComparisonChangeKind.Added,
                    targetParagraph.NodeId,
                    textDiffService.Compare(string.Empty, targetParagraph.Text)));
            }
            else if (sourceParagraph is not null && targetParagraph is null)
            {
                changes.Add(new ComparisonChange(
                    ComparisonChangeKind.Removed,
                    sourceParagraph.NodeId,
                    textDiffService.Compare(sourceParagraph.Text, string.Empty)));
            }
            else if (sourceParagraph is not null && targetParagraph is not null &&
                     !string.Equals(sourceParagraph.Text, targetParagraph.Text, StringComparison.Ordinal))
            {
                changes.Add(new ComparisonChange(
                    ComparisonChangeKind.Modified,
                    targetParagraph.NodeId,
                    textDiffService.Compare(sourceParagraph.Text, targetParagraph.Text)));
            }
        }

        return new ComparisonResult(changes);
    }
}
