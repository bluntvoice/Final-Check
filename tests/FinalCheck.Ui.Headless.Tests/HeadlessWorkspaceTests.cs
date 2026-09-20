using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;
using FinalCheck.App.ViewModels;
using FinalCheck.App.Views;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Ui.Headless.Tests;

public sealed class HeadlessWorkspaceTests
{
    [Fact]
    public async Task RealMainWindowSupportsControlTreeNavigationAndTextInput()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(FinalCheck.App.App));
        await session.Dispatch(() =>
        {
            var model = new MainViewModel();
            var window = new MainWindow { DataContext = model };
            window.Show();
            Assert.True(window.Bounds.Width > 0);
            var quickCompare = Find<Button>(window, "MainQuickCompare");
            Activate(window, quickCompare);
            Assert.True(model.IsSetup);
            Assert.NotNull(Find<Button>(window, "ComparisonStart"));

            model.NavigateCommand.Execute("templates");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var input = Find<TextBox>(window, "TemplateNameInput");
            input.Focus();
            window.KeyTextInput("Headless 示例");
            Assert.Equal("Headless 示例", model.Templates.Name);
            Assert.NotNull(Find<ListBox>(window, "TemplateList"));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RealComparisonViewSelectsItemReviewsAndRestoresWithoutOperatingSystemWindow()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(FinalCheck.App.App));
        await session.Dispatch(() =>
        {
            var result = Fixture();
            var service = new ReviewWorkflow(result.Record);
            var model = new ComparisonResultsViewModel(result, service);
            var window = new Window { Width = 1080, Height = 700, Content = new ComparisonResultsView { DataContext = model } };
            window.Show();
            var list = Find<ListBox>(window, "ComparisonChangeList");
            Assert.Equal(2, list.ItemCount);
            list.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal("second", model.SelectedEntry?.Id);

            var search = Find<TextBox>(window, "ComparisonSearch");
            search.Focus();
            window.KeyTextInput("60");
            Assert.Equal("60", model.SearchText);

            // The current nested member ListBox can transiently clear this selection.
            // The spike does not change product behavior; set the test's target explicitly.
            model.SelectedChange = model.Changes[1];
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Activate(window, Find<Button>(window, "ComparisonReview"));
            Assert.Equal(ComparisonReviewState.Confirmed, model.SelectedChange?.ReviewState);
            Assert.Equal(ComparisonReviewState.Confirmed, service.Record.ReviewStates["second"]);

            var restore = window.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "恢复未处理"));
            Activate(window, restore);
            Assert.Equal(ComparisonReviewState.Unresolved, service.Record.ReviewStates["second"]);
            window.Close();
        }, CancellationToken.None);
    }

    private static T Find<T>(Window window, string automationId) where T : Control =>
        window.GetVisualDescendants().OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static void Activate(Window window, Button button)
    {
        Assert.True(button.Focus());
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
    }

    private static ComparisonWorkflowResult Fixture()
    {
        var file = new ComparisonFile("/fixture.docx", "fixture.docx", 10, DateTimeOffset.UnixEpoch, new string('a', 64));
        var first = Change("first", "30", "60");
        var second = Change("second", "30", "60");
        var record = new ComparisonRecord(1, Guid.NewGuid(), file, file, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UnixEpoch, new Dictionary<string, ComparisonReviewState>
            { ["first"] = ComparisonReviewState.Unresolved, ["second"] = ComparisonReviewState.Unresolved });
        var comparison = new ComparisonResult(1, new("b", "c", 2, 2, "headless-spike"), [], [first, second], [], [],
            ComparisonStatistics.Empty with { TotalChanges = 2, TextChanges = 2 });
        return new(record, DocumentSnapshot.Empty, DocumentSnapshot.Empty, comparison);
    }

    private static ComparisonChangeItem Change(string id, string before, string after) =>
        new(id, ComparisonChangeKind.TextReplace, "p0", "p0", "body/p[0]", "body/p[0]",
            $"付款期限{before}日", $"付款期限{after}日",
            [new(DifferenceOperation.Replace, 4, 2, 4, 2, before, after)], null, [], [], [], ComparisonConfidenceLevel.High, []);

    private sealed class ReviewWorkflow(ComparisonRecord record) : IComparisonWorkflowService
    {
        public ComparisonRecord Record { get; private set; } = record;
        public Task<ComparisonRecord> UpdateReviewAsync(Guid recordId, IReadOnlyList<string> changeIds,
            ComparisonReviewState state, CancellationToken cancellationToken = default)
        {
            var updated = Record.ReviewStates.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var id in changeIds) updated[id] = state;
            Record = Record with { ReviewStates = updated };
            return Task.FromResult(Record);
        }
        public Task<ComparisonInputValidation> ValidateAsync(ComparisonFile baseline, ComparisonFile current,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ComparisonWorkflowResult> ExecuteAsync(ComparisonInputValidation input, IProgress<string>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ComparisonRecord>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ComparisonWorkflowResult?> LoadAsync(Guid recordId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
