using System.Text.Json;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Formatting;

namespace FinalCheck.Comparison.Tests;

public sealed class FormatRestorePlannerTests
{
    [Fact]
    public void GeneratesMergedCharacterAndParagraphItems()
    {
        var baseline = ComparisonFixtureFactory.Create(new ComparisonFixtureFactory.ParagraphSpec("合同", ParagraphFormatting: ParagraphFormatSnapshot.Empty with { Alignment = "center" },
            CharacterFormatting: CharacterFormatSnapshot.Empty with { Bold = true, FontSizeHalfPoints = 24 }));
        var current = ComparisonFixtureFactory.FromText("合同");
        var plan = new SnapshotFormatRestorePlanner().Generate(baseline, current, ComparisonFixtureFactory.Engine().Compare(baseline, current));
        Assert.Equal(2, plan.RestoreItems.Count);
        Assert.Equal(2, Assert.Single(plan.RestoreItems, i => i.Category == FormatRestoreCategory.Character).Difference.Count);
        Assert.All(plan.RestoreItems, i => Assert.Equal(FormatRestoreEligibility.Eligible, i.Eligibility));
    }

    [Fact]
    public void LowConfidenceCannotAutomaticallyRestore()
    {
        var baseline = ComparisonFixtureFactory.Create(new ComparisonFixtureFactory.ParagraphSpec("合同", CharacterFormatting: CharacterFormatSnapshot.Empty with { Bold = true }));
        var current = ComparisonFixtureFactory.FromText("合同");
        var result = ComparisonFixtureFactory.Engine().Compare(baseline, current);
        result = result with { NodeMappings = result.NodeMappings.Select(m => m with { Confidence = ComparisonConfidenceLevel.Low, Score = 0.4 }).ToArray() };
        var plan = new SnapshotFormatRestorePlanner().Generate(baseline, current, result);
        Assert.All(plan.RestoreItems, i => Assert.Equal(FormatRestoreEligibility.NeedsReview, i.Eligibility));
        Assert.Contains(plan.Diagnostics, d => d.Code == "LowConfidenceMapping");
    }

    [Fact]
    public void UnmatchedIsDiagnosedWithoutGuessing()
    {
        var baseline = ComparisonFixtureFactory.FromText("完全不同");
        var current = ComparisonFixtureFactory.FromText("XYZ");
        var plan = new SnapshotFormatRestorePlanner().Generate(baseline, current, ComparisonFixtureFactory.Engine().Compare(baseline, current));
        Assert.Empty(plan.RestoreItems);
        Assert.Contains(plan.Diagnostics, d => d.Code == "UnmappedNode");
    }

    [Fact]
    public void PlanIsDeterministicAndJsonRoundTrips()
    {
        var baseline = ComparisonFixtureFactory.Create(new ComparisonFixtureFactory.ParagraphSpec("合同", CharacterFormatting: CharacterFormatSnapshot.Empty with { Bold = true }));
        var current = ComparisonFixtureFactory.FromText("合同");
        var result = ComparisonFixtureFactory.Engine().Compare(baseline, current);
        var planner = new SnapshotFormatRestorePlanner();
        var plan = planner.Generate(baseline, current, result);
        var json = JsonSerializer.Serialize(plan);
        Assert.Equal(json, JsonSerializer.Serialize(planner.Generate(baseline, current, result)));
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<FormatRestorePlan>(json)));
    }

    [Fact]
    public void CancellationAndStaleComparisonAreRejected()
    {
        var snapshot = ComparisonFixtureFactory.FromText("合同");
        var result = ComparisonFixtureFactory.Engine().Compare(snapshot, snapshot);
        var planner = new SnapshotFormatRestorePlanner();
        Assert.Throws<OperationCanceledException>(() => planner.Generate(snapshot, snapshot, result, cancellationToken: new(true)));
        Assert.Throws<InvalidDataException>(() => planner.Generate(snapshot with { Metadata = snapshot.Metadata with { Sha256 = new string('a', 64) } }, snapshot, result));
    }
}
