namespace FinalCheck.Core.Comparisons;

/// <summary>External input identity only. The original DOCX is never imported into DataRoot.</summary>
public sealed record ComparisonFile(string Path, string Name, long Size, DateTimeOffset ModifiedAt, string Sha256);

public interface IComparisonFileInspector
{
    Task<ComparisonFile> InspectAsync(string path, CancellationToken cancellationToken = default);
}
