using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison;

public sealed record ParagraphMatchResult(
    IReadOnlyList<ComparisonNodeMapping> Mappings,
    IReadOnlyList<DocumentParagraphSnapshot> UnmatchedBaseline,
    IReadOnlyList<DocumentParagraphSnapshot> UnmatchedCurrent,
    IReadOnlyList<ComparisonDiagnostic> Diagnostics);

public interface IStructureMatcher
{
    ParagraphMatchResult Match(
        DocumentSnapshot baseline,
        DocumentSnapshot current,
        CancellationToken cancellationToken = default);
}

public interface ITextDiffService
{
    IReadOnlyList<DifferenceSpan> Compare(
        string baselineText,
        string currentText,
        CancellationToken cancellationToken = default);
}

public interface IComparisonEngine
{
    ComparisonResult Compare(
        DocumentSnapshot baseline,
        DocumentSnapshot current,
        IProgress<ComparisonProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IFormatDiffService
{
    ComparisonFormatDifference? CompareParagraph(
        DocumentParagraphSnapshot baseline,
        DocumentParagraphSnapshot current);

    ComparisonFormatDifference? CompareTable(
        DocumentTableSnapshot baseline,
        DocumentTableSnapshot current);

    ComparisonFormatDifference? CompareCell(
        DocumentTableCellSnapshot baseline,
        DocumentTableCellSnapshot current);
}

public interface IChangeGroupingService
{
    IReadOnlyList<ComparisonChangeGroup> Group(
        IReadOnlyList<ComparisonChangeItem> changes,
        CancellationToken cancellationToken = default);
}
