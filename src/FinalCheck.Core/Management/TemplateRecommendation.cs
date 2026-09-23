using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Core.Management;

public enum TemplateRecommendationKind { None, Unique, Multiple }

public sealed record TemplateRecommendationCandidate(Guid TemplateId, Guid TemplateVersionId, string TemplateName,
    string Version, bool IsCurrent, double Score, ComparisonFile Source)
{
    public string DisplayName => $"{TemplateName} {Version} · {(IsCurrent ? "当前" : "历史")} · {Score:P0}";
}

public sealed record TemplateRecommendation(TemplateRecommendationKind Kind,
    IReadOnlyList<TemplateRecommendationCandidate> Candidates, string Diagnostic);

public interface ITemplateRecommendationService
{
    Task<TemplateRecommendation> RecommendAsync(ComparisonFile current, CancellationToken token = default);
    Task<TemplateRecommendation> RecommendSnapshotAsync(DocumentSnapshot snapshot, string fileName, CancellationToken token = default);
}
