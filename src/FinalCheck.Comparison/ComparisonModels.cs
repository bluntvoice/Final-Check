using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison;

public enum ComparisonChangeKind
{
    Added,
    Removed,
    Modified,
}

public sealed record TextDifference(string OldText, string NewText);

public sealed record ComparisonChange(
    ComparisonChangeKind Kind,
    string NodeId,
    TextDifference Difference);

public sealed record ComparisonResult(IReadOnlyList<ComparisonChange> Changes);

public interface ITextDiffService
{
    TextDifference Compare(string oldText, string newText);
}

public interface IStructureMatcher
{
    IReadOnlyList<(DocumentParagraphSnapshot? Source, DocumentParagraphSnapshot? Target)> Match(
        DocumentSnapshot source,
        DocumentSnapshot target);
}

public interface IComparisonEngine
{
    ComparisonResult Compare(DocumentSnapshot source, DocumentSnapshot target);
}

public interface IFormatDiffService
{
    bool HasFormatDifference(DocumentParagraphSnapshot source, DocumentParagraphSnapshot target);
}

public interface IChangeGroupingService
{
    IReadOnlyList<ComparisonChange> Group(IReadOnlyList<ComparisonChange> changes);
}
