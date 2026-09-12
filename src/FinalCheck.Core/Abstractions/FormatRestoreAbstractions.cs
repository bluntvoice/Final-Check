using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Formatting;

namespace FinalCheck.Core.Abstractions;

public interface IFormatRestorePlanner
{
    FormatRestorePlan Generate(
        DocumentSnapshot baseline, DocumentSnapshot current, ComparisonResult comparison,
        FormatRestorePolicy? policy = null, DateTimeOffset? createdAt = null,
        IProgress<FormatRestoreProgress>? progress = null, CancellationToken cancellationToken = default);
}
