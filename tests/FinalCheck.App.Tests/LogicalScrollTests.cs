using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Tests;

public sealed class LogicalScrollTests
{
    private static WorkspaceDocumentPanel Panel(string name, int count, int insertions = 0) => new(new(name), name,
        Enumerable.Range(0, count + insertions).Select(i => new PreviewBlock(name + i, name + i, [name + i], [], false)).ToArray());
    [Fact] public void OnlyUserPromotesLeaderAndSameAnchorAndFollowerFeedbackDoNotResync()
    {
        var left = Panel("left", 400); var right = Panel("right", 400, 100);
        var links = Enumerable.Range(0, 400).SelectMany(i => new[] { new LogicalNodeLink(left.Id, "left" + i, right.Id, "right" + (i + 100)),
            new LogicalNodeLink(right.Id, "right" + (i + 100), left.Id, "left" + i) }).ToArray();
        var scroll = new LogicalScrollCoordinator([left, right], links); var requests = new List<LogicalScrollRequest>(); scroll.ScrollRequested += requests.Add;
        scroll.UserActivated(left.Id); scroll.ViewportChanged(left.Id, 150); Assert.Equal(new(right.Id, 250), requests.Single());
        scroll.ViewportChanged(right.Id, 250); scroll.ViewportChanged(left.Id, 150); Assert.Single(requests); Assert.Equal(left.Id, scroll.Leader);
        scroll.UserActivated(right.Id); scroll.ViewportChanged(right.Id, 300); Assert.Equal(new(left.Id, 200), requests[1]);
        scroll.SetLinked(false); scroll.ViewportChanged(right.Id, 450); Assert.Equal(2, requests.Count);
        scroll.SetLinked(true); Assert.Equal(new(left.Id, 350), requests[2]); Assert.Equal(right.Id, scroll.Leader);
    }
    [Fact] public void CoordinatorSupportsAdditionalPanelsWithoutAddingTwoSideCoreFields()
    {
        var source = Panel("source", 1); var target = Panel("target", 1); var future = Panel("future", 1);
        var coordinator = new LogicalScrollCoordinator([source, target, future],
            [new(source.Id, "source0", target.Id, "target0"), new(source.Id, "source0", future.Id, "future0")]);
        var requests = new List<LogicalScrollRequest>(); coordinator.ScrollRequested += requests.Add;
        coordinator.UserActivated(source.Id); coordinator.ViewportChanged(source.Id, 0);
        Assert.Equal(2, requests.Count); Assert.DoesNotContain(requests, r => r.Panel == source.Id);
        coordinator.ResetNavigation(); coordinator.ViewportChanged(source.Id, 0); Assert.Equal(2, requests.Count); Assert.Null(coordinator.Leader);
    }
    [Fact] public void AmbiguousMappingAndUnmatchedLongInsertionsAreNotGuessed()
    {
        var left = Panel("left", 10); var right = Panel("right", 10);
        var coordinator = new LogicalScrollCoordinator([left, right], [new(left.Id, "left0", right.Id, "right0"), new(left.Id, "left0", right.Id, "right1")]);
        var requests = new List<LogicalScrollRequest>(); var unmatched = 0; coordinator.ScrollRequested += requests.Add; coordinator.Unmatched += () => unmatched++;
        coordinator.UserActivated(left.Id); coordinator.ViewportChanged(left.Id, 0); coordinator.ViewportChanged(left.Id, 0);
        Assert.Empty(requests); Assert.Equal(1, unmatched);
        coordinator.ViewportChanged(left.Id, 7); Assert.Empty(requests); Assert.Equal(2, unmatched);
    }
}
