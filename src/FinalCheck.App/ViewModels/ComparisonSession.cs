using FinalCheck.Core.Comparisons;

namespace FinalCheck.App.ViewModels;

public enum ComparisonSessionStatus { Setup, Validating, Parsing, Comparing, Saving, Completed, Cancelled, Failed }

public sealed class ComparisonSession
{
    public Guid SessionId { get; } = Guid.NewGuid();
    public ComparisonFile? BaselineFile { get; set; }
    public ComparisonFile? CurrentFile { get; set; }
    public string ParseStatus { get; set; } = "尚未读取";
    public ComparisonSessionStatus Status { get; set; } = ComparisonSessionStatus.Setup;
    public Guid? ResultId { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
    public IReadOnlyList<string> Diagnostics { get; set; } = [];
}
