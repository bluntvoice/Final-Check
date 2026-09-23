using System.Collections.Concurrent;
using System.Text;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Management;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Desktop;

/// <summary>Local, deterministic recommendation from frozen template snapshots; never guesses a legal role.</summary>
public sealed class TemplateRecommendationService(IComparisonFileInspector files, IDocumentParser parser,
    IServiceScopeFactory scopes) : ITemplateRecommendationService
{
    private readonly ConcurrentDictionary<Guid, Features> features = new();
    private readonly ConcurrentQueue<Guid> featureOrder = new();

    public Task<TemplateRecommendation> RecommendAsync(ComparisonFile current, CancellationToken token = default) => Task.Run<TemplateRecommendation>(async () =>
    {
        token.ThrowIfCancellationRequested();
        await using var stream = new FileStream(current.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var snapshot = await parser.ParseAsync(stream, cancellationToken: token);
        if (snapshot.Metadata.Sha256 != current.Sha256 || (await files.InspectAsync(current.Path, token)).Sha256 != current.Sha256)
            throw new IOException("当前文件在模板匹配期间发生变化，请重新选择。 ");
        return await RecommendSnapshotCoreAsync(snapshot, current.Name, token);
    }, token);

    public Task<TemplateRecommendation> RecommendSnapshotAsync(DocumentSnapshot snapshot, string fileName,
        CancellationToken token = default) => Task.Run(() => RecommendSnapshotCoreAsync(snapshot, fileName, token), token);

    private async Task<TemplateRecommendation> RecommendSnapshotCoreAsync(DocumentSnapshot snapshot, string fileName, CancellationToken token)
    {
        if (snapshot.ParseStatus != DocumentParseStatus.Complete)
            return new(TemplateRecommendationKind.None, [], "当前文档解析不完整，未进行自动模板匹配；请手动选择基准。 ");

        var source = Features.From(snapshot, fileName);
        var candidates = new List<TemplateRecommendationCandidate>();
        var skipped = 0;
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ITemplateStore>();
        for (var offset = 0; ; offset += 100)
        {
            var page = await store.ListAsync(offset, 100, token);
            foreach (var template in page.Where(x => x.IsEnabled && !x.IsDeleted))
            {
                var details = await store.GetAsync(template.TemplateId, token);
                foreach (var version in details.Versions.Where(x => x.ParseStatus == DocumentParseStatus.Complete))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (!features.TryGetValue(version.TemplateVersionId, out var target))
                        {
                            var saved = await store.LoadSnapshotAsync(version.TemplateVersionId, token);
                            if (saved.ParseStatus != DocumentParseStatus.Complete) { skipped++; continue; }
                            var computed = Features.From(saved, version.Source.Name);
                            if (features.TryAdd(version.TemplateVersionId, computed))
                            {
                                featureOrder.Enqueue(version.TemplateVersionId);
                                while (features.Count > 256 && featureOrder.TryDequeue(out var oldest)) features.TryRemove(oldest, out _);
                            }
                            target = features.TryGetValue(version.TemplateVersionId, out var cached) ? cached : computed;
                        }
                        var score = Features.Score(source, target, version.IsCurrent);
                        if (score >= 0.60) candidates.Add(new(template.TemplateId, version.TemplateVersionId,
                            template.Name, version.Version, version.IsCurrent, score, version.Source));
                    }
                    catch (Exception error) when (error is InvalidDataException or IOException)
                    { skipped++; }
                }
            }
            if (page.Count < 100) break;
        }
        var ranked = candidates.OrderByDescending(x => x.Score).ThenByDescending(x => x.IsCurrent)
            .ThenBy(x => x.TemplateId).ThenBy(x => x.TemplateVersionId).Take(3).ToArray();
        if (ranked.Length == 0 || ranked[0].Score < 0.78 && ranked.Length == 1)
            return new(TemplateRecommendationKind.None, ranked, $"没有可靠的模板匹配；请手动选择基准。{(skipped > 0 ? $" {skipped} 份模板快照未能参与匹配。" : "")}");
        if (ranked[0].Score >= 0.78 && (ranked.Length == 1 || ranked[0].Score - ranked[1].Score >= 0.08))
            return new(TemplateRecommendationKind.Unique, ranked, "找到高可信唯一模板；预选不等于自动开始比对，仍请核对基准。 ");
        return new(TemplateRecommendationKind.Multiple, ranked, "有多个可信候选；已预选首项，请确认或更换基准后点击开始比对。 ");
    }

    private sealed record Features(string Title, string FileStem, HashSet<string> Headings,
        HashSet<string> Body, HashSet<string> Tables, int Paragraphs)
    {
        public static Features From(DocumentSnapshot snapshot, string fileName)
        {
            var paragraphs = snapshot.Paragraphs.Where(x => !x.IsEmpty).Take(200).Select(x => x.DisplayText).ToArray();
            var title = snapshot.Metadata.Title;
            if (string.IsNullOrWhiteSpace(title)) title = paragraphs.FirstOrDefault(x => x.Length <= 80 && !LooksLikeClause(x));
            title ??= Path.GetFileNameWithoutExtension(fileName);
            var headings = paragraphs.Where(LooksLikeClause).Select(Heading).Where(x => x.Length >= 2)
                .Take(100).ToHashSet(StringComparer.Ordinal);
            var body = Normalize(string.Join(' ', paragraphs));
            if (body.Length > 20000) body = body[..20000];
            var tables = snapshot.Tables.Take(50).Select(x => $"{x.Rows.Count}:{string.Join(',', x.Rows.Take(30).Select(r => r.Cells.Count))}")
                .ToHashSet(StringComparer.Ordinal);
            return new(Normalize(title), Normalize(Path.GetFileNameWithoutExtension(fileName)), headings,
                Shingles(body, 3, 3000), tables, paragraphs.Length);
        }
        private static bool LooksLikeClause(string value)
        {
            var text = value.TrimStart();
            return text.StartsWith('第') && text.IndexOf('条') is > 1 and < 14 ||
                text.Length > 2 && char.IsDigit(text[0]) && (text.Contains('.') || text.Contains('、'));
        }
        private static string Heading(string value)
        {
            var normalized = Normalize(value);
            var result = new StringBuilder(normalized.Length);
            var previousDigit = false;
            foreach (var character in normalized)
            {
                var digit = char.IsDigit(character);
                if (!digit || !previousDigit) result.Append(digit ? '0' : character);
                previousDigit = digit;
            }
            normalized = result.ToString();
            return normalized.Length > 26 ? normalized[..26] : normalized;
        }
        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var result = new StringBuilder(value.Length);
            foreach (var character in value)
                if (char.IsLetterOrDigit(character)) result.Append(char.ToLowerInvariant(character));
            return result.ToString();
        }
        private static HashSet<string> Shingles(string text, int width, int max)
        {
            var values = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i <= text.Length - width && values.Count < max; i++) values.Add(text.Substring(i, width));
            return values;
        }
        private static double Jaccard(HashSet<string> left, HashSet<string> right)
        {
            if (left.Count == 0 || right.Count == 0) return 0;
            var shared = left.Count(x => right.Contains(x));
            return (double)shared / (left.Count + right.Count - shared);
        }
        private static double Text(string left, string right) => left.Length < 3 || right.Length < 3
            ? (left == right && left.Length > 0 ? 1 : 0)
            : Jaccard(Shingles(left, 3, 200), Shingles(right, 3, 200));
        public static double Score(Features source, Features target, bool current)
        {
            var structure = source.Paragraphs == 0 || target.Paragraphs == 0 ? 0 :
                (double)Math.Min(source.Paragraphs, target.Paragraphs) / Math.Max(source.Paragraphs, target.Paragraphs);
            return Math.Min(1, 0.04 * Text(source.FileStem, target.FileStem) + 0.07 * Text(source.Title, target.Title) +
                0.24 * Jaccard(source.Headings, target.Headings) + 0.52 * Jaccard(source.Body, target.Body) +
                0.08 * Jaccard(source.Tables, target.Tables) + 0.05 * structure + (current ? 0.03 : 0));
        }
    }
}
