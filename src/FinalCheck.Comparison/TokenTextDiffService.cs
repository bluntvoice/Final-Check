using FinalCheck.Core.Comparisons;

namespace FinalCheck.Comparison;

public sealed class TokenTextDiffService(ITextTokenizer tokenizer) : ITextDiffService
{
    public IReadOnlyList<DifferenceSpan> Compare(
        string baselineText,
        string currentText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baselineText);
        ArgumentNullException.ThrowIfNull(currentText);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(baselineText, currentText, StringComparison.Ordinal))
        {
            return [];
        }

        var baseline = tokenizer.Tokenize(baselineText);
        var current = tokenizer.Tokenize(currentText);
        var edits = CalculateEdits(baseline, current, cancellationToken);
        return CreateSpans(edits, baselineText, currentText);
    }

    private static List<TokenEdit> CalculateEdits(
        IReadOnlyList<TextToken> baseline,
        IReadOnlyList<TextToken> current,
        CancellationToken cancellationToken)
    {
        var rowCount = baseline.Count + 1;
        var columnCount = current.Count + 1;
        var longestCommonSubsequence = new int[rowCount, columnCount];
        for (var baselineIndex = baseline.Count - 1; baselineIndex >= 0; baselineIndex--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var currentIndex = current.Count - 1; currentIndex >= 0; currentIndex--)
            {
                longestCommonSubsequence[baselineIndex, currentIndex] = string.Equals(
                    baseline[baselineIndex].Value,
                    current[currentIndex].Value,
                    StringComparison.Ordinal)
                    ? longestCommonSubsequence[baselineIndex + 1, currentIndex + 1] + 1
                    : Math.Max(
                        longestCommonSubsequence[baselineIndex + 1, currentIndex],
                        longestCommonSubsequence[baselineIndex, currentIndex + 1]);
            }
        }

        var edits = new List<TokenEdit>();
        var baselinePosition = 0;
        var currentPosition = 0;
        while (baselinePosition < baseline.Count || currentPosition < current.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (baselinePosition < baseline.Count &&
                currentPosition < current.Count &&
                string.Equals(
                    baseline[baselinePosition].Value,
                    current[currentPosition].Value,
                    StringComparison.Ordinal))
            {
                edits.Add(new TokenEdit(TokenEditKind.Equal, baseline[baselinePosition], current[currentPosition]));
                baselinePosition++;
                currentPosition++;
            }
            else if (currentPosition < current.Count &&
                     (baselinePosition == baseline.Count ||
                      longestCommonSubsequence[baselinePosition, currentPosition + 1] >=
                      longestCommonSubsequence[baselinePosition + 1, currentPosition]))
            {
                edits.Add(new TokenEdit(TokenEditKind.Insert, null, current[currentPosition]));
                currentPosition++;
            }
            else
            {
                edits.Add(new TokenEdit(TokenEditKind.Delete, baseline[baselinePosition], null));
                baselinePosition++;
            }
        }

        return edits;
    }

    private static List<DifferenceSpan> CreateSpans(
        List<TokenEdit> edits,
        string baselineText,
        string currentText)
    {
        var spans = new List<DifferenceSpan>();
        var baselineCursor = 0;
        var currentCursor = 0;
        var baselineStart = 0;
        var currentStart = 0;
        var baselineLength = 0;
        var currentLength = 0;

        void Flush()
        {
            if (baselineLength == 0 && currentLength == 0)
            {
                return;
            }

            var operation = baselineLength == 0
                ? DifferenceOperation.Insert
                : currentLength == 0
                    ? DifferenceOperation.Delete
                    : DifferenceOperation.Replace;
            spans.Add(new DifferenceSpan(
                operation,
                baselineStart,
                baselineLength,
                currentStart,
                currentLength,
                baselineText.Substring(baselineStart, baselineLength),
                currentText.Substring(currentStart, currentLength)));
            baselineLength = 0;
            currentLength = 0;
        }

        foreach (var edit in edits)
        {
            if (edit.Kind == TokenEditKind.Equal)
            {
                Flush();
                baselineCursor += edit.Baseline!.Length;
                currentCursor += edit.Current!.Length;
                continue;
            }

            if (baselineLength == 0 && currentLength == 0)
            {
                baselineStart = baselineCursor;
                currentStart = currentCursor;
            }

            if (edit.Kind == TokenEditKind.Delete)
            {
                baselineLength += edit.Baseline!.Length;
                baselineCursor += edit.Baseline.Length;
            }
            else
            {
                currentLength += edit.Current!.Length;
                currentCursor += edit.Current.Length;
            }
        }

        Flush();
        return spans;
    }

    private enum TokenEditKind
    {
        Equal,
        Insert,
        Delete,
    }

    private sealed record TokenEdit(TokenEditKind Kind, TextToken? Baseline, TextToken? Current);
}
