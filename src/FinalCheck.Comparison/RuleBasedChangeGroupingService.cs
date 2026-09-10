using System.Security.Cryptography;
using System.Text;
using FinalCheck.Core.Comparisons;

namespace FinalCheck.Comparison;

public sealed class RuleBasedChangeGroupingService : IChangeGroupingService
{
    public IReadOnlyList<ComparisonChangeGroup> Group(
        IReadOnlyList<ComparisonChangeItem> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var candidates = changes
            .Select(change => (Change: change, Key: GroupKey(change)))
            .Where(item => item.Key is not null)
            .GroupBy(item => item.Key!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        var groups = new List<ComparisonChangeGroup>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = candidate.Select(item => item.Change)
                .OrderBy(change => change.ChangeId, StringComparer.Ordinal)
                .ToArray();
            var first = items[0];
            var span = first.DifferenceSpans[0];
            groups.Add(new ComparisonChangeGroup(
                "group:" + Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Key))).ToLowerInvariant()[..16],
                first.Kind,
                span.OldText,
                span.NewText,
                items.Select(item => item.ChangeId).ToArray()));
        }

        return groups;
    }

    private static string? GroupKey(ComparisonChangeItem change)
    {
        if (change.DifferenceSpans.Count != 1)
        {
            return null;
        }

        var span = change.DifferenceSpans[0];
        return string.IsNullOrEmpty(span.OldText) && string.IsNullOrEmpty(span.NewText)
            ? null
            : $"{change.Kind}\u001f{span.OldText}\u001f{span.NewText}";
    }
}
