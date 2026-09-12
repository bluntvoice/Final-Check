using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Comparison;

public sealed class WorkingCopyComparisonService(IComparisonEngine engine) : IWorkingCopyComparisonService
{
    public ComparisonResult Compare(DocumentSnapshot baseline, DocumentSnapshot current, CancellationToken cancellationToken = default) =>
        engine.Compare(baseline, current, cancellationToken: cancellationToken);
}
