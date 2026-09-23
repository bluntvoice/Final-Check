using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FinalCheck.App.ViewModels;
using FinalCheck.App.Views;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using Xunit.Abstractions;

namespace FinalCheck.App.Tests;

[CollectionDefinition("AvaloniaHeadless", DisableParallelization = true)]
public sealed class AvaloniaHeadlessTestGroup { }

[Collection("AvaloniaHeadless")]
public sealed class ComparisonWorkspaceHeadlessTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SelectingAnotherChangeInRealViewKeepsTheSharedSelection()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(FinalCheck.App.App));
        await session.Dispatch(() =>
        {
            var model = new ComparisonResultsViewModel(ComparisonResultsTests.Result(
                ComparisonResultsTests.Change(), ComparisonResultsTests.Change("two")));
            var window = new Window
            {
                Width = 1300,
                Height = 800,
                Content = new ComparisonResultsView { DataContext = model },
            };
            window.Show();
            var changeList = window.GetVisualDescendants().OfType<ListBox>()
                .Single(list => AutomationProperties.GetName(list) == "修改清单");
            Assert.Equal(2, changeList.ItemCount);
            changeList.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal("two", model.SelectedEntry?.Id);
            Assert.Equal("two", model.SelectedChange?.ChangeId);

            model.SearchText = "30";
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal("two", model.SelectedChange?.ChangeId);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectingGroupedMemberAndChangingViewKeepsTheSameChange()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(FinalCheck.App.App));
        await session.Dispatch(() =>
        {
            var first = ComparisonResultsTests.Change();
            var second = ComparisonResultsTests.Change("two");
            var result = ComparisonResultsTests.Result(first, second);
            result = result with { Result = result.Result with
            {
                Groups = [new("group", ComparisonChangeKind.TextReplace, "30", "60", ["one", "two"])],
            } };
            var model = new ComparisonResultsViewModel(result);
            var window = new Window
            {
                Width = 1300,
                Height = 800,
                Content = new ComparisonResultsView { DataContext = model },
            };
            window.Show();
            var members = window.GetVisualDescendants().OfType<ListBox>()
                .Single(list => AutomationProperties.GetName(list) == "归并组内修改位置");
            Assert.Equal(2, members.ItemCount);
            members.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal("two", model.SelectedChange?.ChangeId);

            model.Grouped = false;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal("two", model.SelectedEntry?.Id);
            Assert.Equal("two", model.SelectedChange?.ChangeId);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task GroupedPreviousAndNextKeepTheActualMemberSelection()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(FinalCheck.App.App));
        await session.Dispatch(() =>
        {
            var result = ComparisonResultsTests.Result(
                ComparisonResultsTests.Change(), ComparisonResultsTests.Change("two"));
            result = result with { Result = result.Result with
            {
                Groups = [new("group", ComparisonChangeKind.TextReplace, "30", "60", ["one", "two"])],
            } };
            var model = new ComparisonResultsViewModel(result);
            var window = new Window
            {
                Width = 1300,
                Height = 800,
                Content = new ComparisonResultsView { DataContext = model },
            };
            window.Show();
            Assert.Equal("one", model.SelectedChange?.ChangeId);
            model.NextChangeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal("two", model.SelectedChange?.ChangeId);
            Assert.True(model.CanPrevious);
            model.PreviousChangeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal("one", model.SelectedChange?.ChangeId);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LongGroupedListKeepsPreviousNavigationAfterVirtualizedMemberRefresh()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(FinalCheck.App.App));
        await session.Dispatch(() =>
        {
            var changes = Enumerable.Range(0, 52).Select(i => ComparisonResultsTests.Change($"change-{i}") with
            {
                BaselineNodeId = $"b{i}",
                CurrentNodeId = $"c{i}",
                BaselineLocation = $"body/p[{i}]",
                CurrentLocation = $"body/p[{i + 7}]",
            }).ToArray();
            var result = ComparisonResultsTests.Result(changes);
            result = result with
            {
                Baseline = DocumentSnapshot.Empty with { Paragraphs = Enumerable.Range(0, 52)
                    .Select(i => ComparisonPreviewTests.Paragraph($"b{i}", i)).ToArray() },
                Current = DocumentSnapshot.Empty with { Paragraphs = Enumerable.Range(0, 59)
                    .Select(i => ComparisonPreviewTests.Paragraph($"c{i}", i)).ToArray() },
                Result = result.Result with
                {
                    Groups = [new("group", ComparisonChangeKind.TextReplace, "30", "60",
                        changes.Select(change => change.ChangeId).ToArray())],
                },
            };
            var model = new ComparisonResultsViewModel(result);
            var window = new Window
            {
                Width = 1080,
                Height = 680,
                Content = new ComparisonResultsView { DataContext = model },
            };
            window.Show();
            var details = window.GetVisualDescendants().OfType<ScrollViewer>().Single(view => view.Name == "DetailArea");
            string location() => details.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => AutomationProperties.GetAutomationId(block) == "SelectedChangeLocation").Text!;
            Assert.Equal("change-0", model.SelectedChange?.ChangeId);
            var firstLocation = location();
            model.NextChangeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal("change-1", model.SelectedChange?.ChangeId);
            Assert.NotEqual(firstLocation, location());
            model.PreviousChangeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal("change-0", model.SelectedChange?.ChangeId);
            Assert.Equal(firstLocation, location());
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LargeWorkspaceUsesVirtualizedPreviewControls()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(FinalCheck.App.App));
        await session.Dispatch(() =>
        {
            var changes = Enumerable.Range(0, 368).Select(i => ComparisonResultsTests.Change($"change-{i}") with
            {
                BaselineNodeId = $"b{i}",
                CurrentNodeId = $"c{i}",
            }).ToArray();
            var result = ComparisonResultsTests.Result(changes);
            result = result with
            {
                Baseline = DocumentSnapshot.Empty with { Paragraphs = Enumerable.Range(0, 400)
                    .Select(i => ComparisonPreviewTests.Paragraph($"b{i}", i)).ToArray() },
                Current = DocumentSnapshot.Empty with { Paragraphs = Enumerable.Range(0, 508)
                    .Select(i => ComparisonPreviewTests.Paragraph($"c{i}", i)).ToArray() },
            };
            var watch = Stopwatch.StartNew();
            var model = new ComparisonResultsViewModel(result);
            var window = new Window
            {
                Width = 1400,
                Height = 850,
                Content = new ComparisonResultsView { DataContext = model },
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var lists = window.GetVisualDescendants().OfType<ListBox>().ToArray();
            var changeList = lists.Single(list => AutomationProperties.GetName(list) == "修改清单");
            var baselineList = lists.Single(list => AutomationProperties.GetName(list) == "基准文档预览");
            var currentList = lists.Single(list => AutomationProperties.GetName(list) == "当前文档预览");
            Assert.Equal(368, changeList.ItemCount);
            Assert.Equal(400, baselineList.ItemCount);
            Assert.Equal(508, currentList.ItemCount);
            Assert.InRange(baselineList.GetRealizedContainers().Count(), 1, 100);
            Assert.InRange(currentList.GetRealizedContainers().Count(), 1, 100);
            Assert.Equal("change-0", model.SelectedChange?.ChangeId);
            watch.Stop();
            output.WriteLine($"368 changes / 400+508 paragraphs, real Headless layout: {watch.Elapsed.TotalMilliseconds:F1} ms.");
            window.Close();
        }, CancellationToken.None);
    }
}
