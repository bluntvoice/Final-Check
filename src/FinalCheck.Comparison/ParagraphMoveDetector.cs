using FinalCheck.Core.Comparisons;

namespace FinalCheck.Comparison;

public sealed class ParagraphMoveDetector : IParagraphMoveDetector
{
    public IReadOnlyList<ComparisonNodeMapping> Detect(
        IReadOnlyList<ComparisonNodeMapping> mappings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        if (mappings.Count < 2)
        {
            return mappings.ToArray();
        }

        var ordered = mappings
            .OrderBy(mapping => mapping.BaselineNode.Position)
            .ThenBy(mapping => mapping.CurrentNode.Position)
            .ToArray();
        var stableIndexes = LongestIncreasingSubsequence(ordered, cancellationToken);
        var result = new ComparisonNodeMapping[ordered.Length];
        for (var index = 0; index < ordered.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mapping = ordered[index];
            var duplicateOrderMapping = mapping.MappingType == ComparisonMappingType.Contextual &&
                                        mapping.Confidence == ComparisonConfidenceLevel.Medium;
            result[index] = mapping with { IsMoved = !stableIndexes.Contains(index) && !duplicateOrderMapping };
        }

        return result;
    }

    private static HashSet<int> LongestIncreasingSubsequence(
        ComparisonNodeMapping[] mappings,
        CancellationToken cancellationToken)
    {
        var tails = new int[mappings.Length];
        var previous = new int[mappings.Length];
        Array.Fill(previous, -1);
        var length = 0;
        for (var index = 0; index < mappings.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var low = 0;
            var high = length;
            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                if (mappings[tails[middle]].CurrentNode.Position < mappings[index].CurrentNode.Position)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            if (low > 0)
            {
                previous[index] = tails[low - 1];
            }

            tails[low] = index;
            if (low == length)
            {
                length++;
            }
        }

        var result = new HashSet<int>();
        for (var index = length == 0 ? -1 : tails[length - 1]; index >= 0; index = previous[index])
        {
            result.Add(index);
            if (previous[index] < 0)
            {
                break;
            }
        }

        return result;
    }
}
