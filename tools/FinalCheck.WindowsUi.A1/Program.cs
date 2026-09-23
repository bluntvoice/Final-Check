using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.UIA3;

namespace FinalCheck.WindowsUi.A1;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed record ControlInfo(string Name, string AutomationId, string Type, bool Enabled);
    private sealed record CheckInfo(string Name, string Detail);
    private sealed record Report(bool Success, string Stage, string? Error, int? ProcessId,
        string? WindowTitle, CheckInfo[] Checks, ControlInfo[] Controls, long DurationMs);

    private static int Main(string[] args)
    {
        if (args.Length != 4 || args[0] != "--app" || args[2] != "--data-root" ||
            !Path.IsPathFullyQualified(args[1]) || !Path.IsPathFullyQualified(args[3]))
        {
            Emit(new(false, "arguments", "Usage: --app <Debug Desktop.exe> --data-root <existing isolated fixture>",
                null, null, [], [], 0));
            return 2;
        }

        var appPath = Path.GetFullPath(args[1]);
        var dataRoot = Path.GetFullPath(args[3]);
        var temp = Path.GetFullPath(Path.GetTempPath());
        if (!OperatingSystem.IsWindows() || !File.Exists(appPath) ||
            !string.Equals(Path.GetFileName(appPath), "FinalCheck.Desktop.exe", StringComparison.OrdinalIgnoreCase) ||
            !appPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetRelativePath(temp, dataRoot).StartsWith("FinalCheck-StageA1-", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(dataRoot, "finalcheck.db")) ||
            !File.Exists(Path.Combine(dataRoot + ".fixtures", "workspace-baseline.docx")) ||
            !File.Exists(Path.Combine(dataRoot + ".fixtures", "workspace-current.docx")))
        {
            Emit(new(false, "arguments", "Only a Debug app and an existing generated Stage A1 fixture under Temp are accepted.",
                null, null, [], [], 0));
            return 2;
        }

        var watch = Stopwatch.StartNew();
        var stage = "launch";
        int? processId = null;
        string? title = null;
        string? error = null;
        ControlInfo[] controls = [];
        var checks = new List<CheckInfo>();
        var issues = new List<string>();
        Application? app = null;
        try
        {
            var start = new ProcessStartInfo(appPath) { WorkingDirectory = Path.GetDirectoryName(appPath)!, UseShellExecute = false };
            start.ArgumentList.Add("--developer-data-directory");
            start.ArgumentList.Add(dataRoot);
            app = Application.Launch(start);
            processId = app.ProcessId;
            using var automation = new UIA3Automation();
            stage = "window";
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(20))
                ?? throw new InvalidOperationException("No main window appeared within 20 seconds.");
            title = window.Title;
            stage = "home-tree";
            controls = ReadTree(window);
            var recent = window.FindFirstDescendant(cf => cf.ByName("继续最近一次比对"))?.AsButton()
                ?? throw new InvalidOperationException("Recent comparison button was not found.");
            stage = "open-recent";
            recent.Invoke();
            var wait = Stopwatch.StartNew();
            AutomationElement? changeList = null;
            while (wait.Elapsed < TimeSpan.FromSeconds(20) && changeList is null)
            {
                changeList = window.FindFirstDescendant(cf => cf.ByName("修改清单"));
                if (changeList is null) Thread.Sleep(200);
            }
            if (changeList is null) throw new InvalidOperationException("Comparison workspace did not appear within 20 seconds.");
            stage = "workspace-tree";
            controls = ReadTree(window);
            var stats = Stats(window);
            Require(stats.Contains("总变化 368", StringComparison.Ordinal), $"Unexpected workspace statistics: {stats}");
            Require(Named(window, "基准版本上下文").BoundingRectangle.Width > 50, "Baseline context is not visible by default.");
            Require(Named(window, "当前版本上下文").BoundingRectangle.Width > 50, "Current context is not visible by default.");
            checks.Add(new("workspace", $"{stats}; detail={DetailLocation(window)}"));

            stage = "list-click-location";
            var firstDetail = DetailLocation(window);
            var initialItems = changeList.AsListBox().Items;
            Require(initialItems.Length >= 2, "Two comparison list entries were not realized for the click test.");
            window.Focus();
            initialItems[1].ScrollIntoView();
            Thread.Sleep(150);
            initialItems[1].Click();
            Thread.Sleep(250);
            var clickedDetail = DetailLocation(window);
            Require(clickedDetail != firstDetail, "Clicking another change did not refresh the selected detail.");
            Named(window, "修改清单").AsListBox().Items[0].Click();
            Thread.Sleep(250);
            Require(DetailLocation(window) == firstDetail, "Clicking the first list entry did not restore its detail.");
            checks.Add(new("list-click-location", $"first={firstDetail}; clicked={clickedDetail}; restored=true"));

            stage = "next-previous";
            var before = DetailLocation(window);
            Named(window, "下一项").AsButton().Invoke();
            Thread.Sleep(500);
            var after = DetailLocation(window);
            Require(before != after && after.Length > 0, "Next did not change the selected comparison item.");
            var previousEnabled = Named(window, "上一项").IsEnabled;
            Named(window, "上一项").AsButton().Invoke();
            Thread.Sleep(500);
            var returned = DetailLocation(window);
            if (returned != before) issues.Add($"Previous did not return to the original detail: enabled={previousEnabled}; before={before}; next={after}; previous={returned}.");
            checks.Add(new("next-previous", $"before={before}; next={after}; previous={returned}; enabled={previousEnabled}"));

            stage = "review-undo";
            var initial = Stats(window);
            Named(window, "审阅当前项").AsButton().Invoke();
            Thread.Sleep(200);
            var reviewed = Stats(window);
            Require(reviewed != initial && reviewed.Contains("已审阅 1", StringComparison.Ordinal), $"Review state did not update: {reviewed}");
            Named(window, "撤销上次状态操作").AsButton().Invoke();
            Thread.Sleep(200);
            Require(Stats(window) == initial, "Undo did not restore initial review statistics.");
            checks.Add(new("review-undo", "review changed counters and undo restored them"));

            stage = "ignore-undo";
            Named(window, "忽略当前项").AsButton().Invoke();
            Thread.Sleep(200);
            Require(Stats(window).Contains("忽略 1", StringComparison.Ordinal), "Ignore did not update statistics.");
            Named(window, "撤销上次状态操作").AsButton().Invoke();
            Thread.Sleep(200);
            Require(Stats(window) == initial, "Ignore undo did not restore initial statistics.");
            checks.Add(new("ignore-undo", "ignore changed counters and undo restored them"));

            stage = "grouping-search";
            var grouped = Named(window, "归并视图（取消勾选查看逐项）").AsCheckBox();
            Require(grouped.IsChecked == true, "Grouped view was not initially checked.");
            var groupedEntry = Named(window, "修改清单").AsListBox().Items[0].Name;
            Require(groupedEntry.Contains("Id = group:", StringComparison.Ordinal), "The first grouped entry did not expose its group identity.");
            grouped.IsChecked = false;
            Thread.Sleep(150);
            Require(grouped.IsChecked == false, "Individual view did not activate.");
            var individualEntry = Named(window, "修改清单").AsListBox().Items[0].Name;
            Require(!individualEntry.Contains("Id = group:", StringComparison.Ordinal), "Individual view still shows a group entry.");
            var search = Named(window, "搜索修改").AsTextBox();
            search.Text = "付款期限";
            Thread.Sleep(200);
            var searchCount = Count(window);
            Require(searchCount != "368 / 368 项" && !searchCount.StartsWith("0 /", StringComparison.Ordinal),
                $"Text search did not reduce the visible result set: {searchCount}");
            search.Text = "合成批注";
            Thread.Sleep(200);
            var commentCount = Count(window);
            Require(commentCount.StartsWith("1 / ", StringComparison.Ordinal), $"Comment search did not isolate one result: {commentCount}");
            Require(window.FindAllDescendants().Any(e => Safe(() => e.Name).Contains("合成批注：请核实付款期限", StringComparison.Ordinal)),
                "The selected comment text was not available in the workspace.");
            search.Text = "";
            Thread.Sleep(150);
            Named(window, "状态筛选").AsComboBox().Select("已忽略");
            Thread.Sleep(150);
            Require(Count(window).StartsWith("0 / ", StringComparison.Ordinal), "Ignored-only filter unexpectedly displayed unresolved changes.");
            Named(window, "状态筛选").AsComboBox().Select("全部");
            Named(window, "类型筛选").AsComboBox().Select("文字");
            Thread.Sleep(150);
            var typeCount = Count(window);
            Require(typeCount != "368 / 368 项" && !typeCount.StartsWith("0 /", StringComparison.Ordinal),
                $"Text type filter did not reduce results: {typeCount}");
            Named(window, "类型筛选").AsComboBox().Select("全部类型");
            grouped.IsChecked = true;
            Keyboard.PressVirtualKeyCode(0x1B);
            Keyboard.PressVirtualKeyCode(0x1B);
            var windowBounds = window.BoundingRectangle;
            Mouse.Click(new System.Drawing.Point((int)(windowBounds.Left + 500), (int)(windowBounds.Top + 100)), MouseButton.Left);
            Thread.Sleep(400);
            checks.Add(new("grouping-search", $"grouped/individual switched; text={searchCount}; comment={commentCount}; text type={typeCount}; ignored-only=0"));

            stage = "context-to-full";
            var contextCapture = Path.Combine(dataRoot + ".fixtures", "context-compact-" + Guid.NewGuid().ToString("N") + ".png");
            using (var capture = Capture.Element(window)) capture.ToFile(contextCapture);
            Named(window, "查看完整文档 / 全文对照").AsButton().Invoke();
            Thread.Sleep(250);
            var baseline = Named(window, "基准文档预览").AsListBox();
            var current = Named(window, "当前文档预览").AsListBox();
            Require(baseline.BoundingRectangle.Width > 50 && current.BoundingRectangle.Width > 50,
                "Full-document comparison did not open both previews.");
            checks.Add(new("context-to-full", $"the selected change remained in the shared workspace; both full previews opened; context capture={contextCapture}"));

            stage = "responsive-layout";
            var compactPreview = baseline.BoundingRectangle;
            var compactCapture = Path.Combine(dataRoot + ".fixtures", "a1-compact-" + Guid.NewGuid().ToString("N") + ".png");
            using (var capture = Capture.Element(window)) capture.ToFile(compactCapture);
            var maximize = window.FindFirstDescendant(cf => cf.ByAutomationId("Maximize-Restore"))?.AsButton();
            if (maximize is not null && maximize.Name == "最大化") maximize.Invoke();
            Thread.Sleep(400);
            var expandedPreview = baseline.BoundingRectangle;
            var expandedCapture = Path.Combine(dataRoot + ".fixtures", "a1-maximized-" + Guid.NewGuid().ToString("N") + ".png");
            using (var capture = Capture.Element(window)) capture.ToFile(expandedCapture);
            var detailBounds = window.FindFirstDescendant(cf => cf.ByAutomationId("DetailArea"))?.BoundingRectangle
                ?? throw new InvalidOperationException("Detail area was not found after maximizing.");
            Require(expandedPreview.Height > compactPreview.Height && expandedPreview.Height > 150,
                $"Maximized comparison preview did not gain a usable viewport: compact={compactPreview}; maximized={expandedPreview}.");
            checks.Add(new("responsive-layout", $"compact preview={compactPreview.Width:F0}x{compactPreview.Height:F0}; maximized preview={expandedPreview.Width:F0}x{expandedPreview.Height:F0}; detail={detailBounds.Width:F0}x{detailBounds.Height:F0}; captures={compactCapture} | {expandedCapture}"));

            stage = "scroll-baseline";
            var leftScroll = ScrollBar(baseline);
            var rightScroll = ScrollBar(current);
            var leftBefore = leftScroll.Value;
            var rightBefore = rightScroll.Value;
            var leftParagraphBefore = VisibleParagraph(baseline);
            var rightParagraphBefore = VisibleParagraph(current);
            var rect = baseline.BoundingRectangle;
            Require(rect.Width > 50 && rect.Height > 50,
                $"Baseline preview has no usable on-screen viewport after filtering: {rect}.");
            window.Focus();
            baseline.Focus();
            Mouse.Click(new System.Drawing.Point((int)(rect.Left + rect.Width / 2),
                (int)(rect.Top + rect.Height / 2)), MouseButton.Left);
            Mouse.MoveTo((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2));
            Mouse.Scroll(-2);
            Thread.Sleep(350);
            var leftAfter = leftScroll.Value;
            var rightAfter = rightScroll.Value;
            Require(leftAfter > leftBefore, $"Baseline wheel input did not scroll: {leftBefore} -> {leftAfter}; viewport={rect}.");
            checks.Add(new("scroll-baseline", $"wheel baseline {leftBefore:F1}->{leftAfter:F1}; follower {rightBefore:F1}->{rightAfter:F1}; selected={leftParagraphBefore}/{rightParagraphBefore}"));

            stage = "link-off-on";
            var linked = Named(window, "逻辑联动滚动（取消勾选解除联动）").AsCheckBox();
            Require(linked.IsChecked == true, "Linked scrolling was not initially enabled.");
            linked.IsChecked = false;
            Thread.Sleep(100);
            Require(linked.IsChecked == false, "Linked scrolling did not switch off.");
            var independentLeft = leftScroll.Value;
            var independentRight = rightScroll.Value;
            Mouse.MoveTo((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2));
            Mouse.Scroll(-15);
            Thread.Sleep(300);
            Require(leftScroll.Value > independentLeft, "Baseline did not scroll with linking disabled.");
            Require(Math.Abs(rightScroll.Value - independentRight) < 0.5,
                $"Follower moved while linking was disabled: {independentRight:F1} -> {rightScroll.Value:F1}.");
            linked.IsChecked = true;
            Thread.Sleep(300);
            Require(linked.IsChecked == true, "Linked scrolling did not switch on again.");
            Require(Math.Abs(rightScroll.Value - independentRight) > 1,
                "Re-enabling linked scrolling did not realign the follower after a different leader anchor.");
            checks.Add(new("link-off-on", $"off baseline {independentLeft:F1}->{leftScroll.Value:F1}; follower held {independentRight:F1}; reenabled follower={rightScroll.Value:F1}"));

            stage = "fast-wheel";
            var fastBefore = leftScroll.Value;
            Mouse.MoveTo((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2));
            Mouse.Scroll(-12);
            Thread.Sleep(400);
            var fastAfter = leftScroll.Value;
            Require(fastAfter > fastBefore, "Fast wheel input did not move the baseline preview.");
            var followerAfterFast = rightScroll.Value;
            Thread.Sleep(300);
            Require(Math.Abs(rightScroll.Value - followerAfterFast) < 1,
                "Follower kept moving after the high-frequency input settled.");
            checks.Add(new("fast-wheel", $"baseline {fastBefore:F1}->{fastAfter:F1}; follower settled at {followerAfterFast:F1}"));

            stage = "scrollbar-drag";
            var thumb = (leftScroll.FindFirstDescendant(cf => cf.ByName("Position"))
                ?? throw new InvalidOperationException("Baseline scrollbar thumb was not found.")).BoundingRectangle;
            var dragBefore = leftScroll.Value;
            var dragFollowerBefore = rightScroll.Value;
            Mouse.DragVertically(new System.Drawing.Point((int)(thumb.Left + thumb.Width / 2),
                (int)(thumb.Top + thumb.Height / 2)), 40, MouseButton.Left);
            Thread.Sleep(400);
            Require(Math.Abs(leftScroll.Value - dragBefore) > 1, "Dragging the baseline scrollbar did not move its preview.");
            if (Math.Abs(rightScroll.Value - dragFollowerBefore) < 1)
                Require(PreviewNotice(window).Length > 0, "Scrollbar drag left the follower unchanged without an unmatched-mapping diagnostic.");
            checks.Add(new("scrollbar-drag", $"baseline {dragBefore:F1}->{leftScroll.Value:F1}; follower {dragFollowerBefore:F1}->{rightScroll.Value:F1}; notice={PreviewNotice(window)}"));

            stage = "switch-leader";
            var rightRect = current.BoundingRectangle;
            var switchBefore = rightScroll.Value;
            Mouse.MoveTo((int)(rightRect.Left + rightRect.Width / 2), (int)(rightRect.Top + rightRect.Height / 2));
            Mouse.Scroll(-1);
            Thread.Sleep(350);
            Require(rightScroll.Value > switchBefore, "Current-side wheel input did not move the current preview.");
            checks.Add(new("switch-leader", $"current {switchBefore:F1}->{rightScroll.Value:F1}; baseline={leftScroll.Value:F1}"));

            stage = "full-to-context";
            var contextDetail = DetailLocation(window);
            Named(window, "返回逐条审阅").AsButton().Invoke();
            Thread.Sleep(250);
            Require(Named(window, "基准版本上下文").BoundingRectangle.Width > 50 &&
                Named(window, "当前版本上下文").BoundingRectangle.Width > 50,
                "Returning from full-document mode did not restore both context panels.");
            Require(DetailLocation(window) == contextDetail, "Returning to context changed the selected item.");
            checks.Add(new("full-to-context", "both context panels returned with the same selected change"));

            stage = "persist-review";
            var reviewedDetail = DetailLocation(window);
            Named(window, "审阅当前项").AsButton().Invoke();
            Thread.Sleep(200);
            Require(Stats(window).Contains("已审阅 1", StringComparison.Ordinal), "Review was not saved before restart.");
            app.Close();
            Require(WaitForExit(processId.Value, TimeSpan.FromSeconds(8)), "Desktop did not exit normally before restart.");
            app.Dispose();
            app = Application.Launch(start);
            processId = app.ProcessId;
            window = app.GetMainWindow(automation, TimeSpan.FromSeconds(20))
                ?? throw new InvalidOperationException("No main window appeared after restart.");
            Named(window, "继续最近一次比对").AsButton().Invoke();
            var reopenWait = Stopwatch.StartNew();
            while (reopenWait.Elapsed < TimeSpan.FromSeconds(20) &&
                   window.FindFirstDescendant(cf => cf.ByName("修改清单")) is null) Thread.Sleep(200);
            Require(window.FindFirstDescendant(cf => cf.ByName("修改清单")) is not null,
                "Comparison workspace did not reopen after restart.");
            Require(Stats(window).Contains("已审阅 1", StringComparison.Ordinal),
                "Reviewed state was not restored after application restart.");
            Named(window, "状态筛选").AsComboBox().Select("已审阅");
            Thread.Sleep(200);
            Require(DetailLocation(window) == reviewedDetail,
                $"Reviewed-only filter did not select the persisted item: expected={reviewedDetail}; actual={DetailLocation(window)}.");
            Named(window, "恢复未处理").AsButton().Invoke();
            Thread.Sleep(200);
            Require(Stats(window).Contains("已审阅 0", StringComparison.Ordinal),
                "Restoring unresolved after restart did not persist the expected final state.");
            checks.Add(new("review-restart", "one reviewed item survived normal restart; restored to unresolved afterward"));

            stage = "complete";
            if (issues.Count > 0) throw new InvalidDataException(string.Join(" ", issues));
        }
        catch (Exception exception)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
        }
        finally
        {
            if (app is not null)
            {
                try { if (!app.HasExited) app.Close(); } catch { /* Preserve failure evidence and process ID. */ }
                app.Dispose();
            }
        }
        watch.Stop();
        Emit(new(error is null, stage, error, processId, title, checks.ToArray(), error is null ? [] : controls, watch.ElapsedMilliseconds));
        return error is null ? 0 : 1;
    }

    private static ControlInfo[] ReadTree(Window window) => window.FindAllDescendants()
        .Where(e => Safe(() => e.ControlType.ToString()) is "Button" or "CheckBox" or "List" or "Edit" or "ScrollBar")
        .Take(80)
        .Select(e => new ControlInfo(Safe(() => e.Name), Safe(() => e.AutomationId),
            Safe(() => e.ControlType.ToString()), Safe(() => e.IsEnabled)))
        .ToArray();

    private static string DetailLocation(Window window) => Safe(() =>
        window.FindFirstDescendant(cf => cf.ByAutomationId("SelectedChangeLocation"))?.Name);

    private static AutomationElement Named(Window window, string name) =>
        window.FindFirstDescendant(cf => cf.ByName(name))
        ?? throw new InvalidOperationException($"Expected UI control was not found: {name}");

    private static string Stats(Window window) => window.FindAllDescendants()
        .Select(e => Safe(() => e.Name))
        .FirstOrDefault(name => name.StartsWith("总变化 ", StringComparison.Ordinal)) ?? "";

    private static string Count(Window window) => window.FindAllDescendants()
        .Select(e => Safe(() => e.Name))
        .FirstOrDefault(name => name.Contains(" / ", StringComparison.Ordinal) && name.EndsWith(" 项", StringComparison.Ordinal)) ?? "";

    private static string VisibleParagraph(ListBox list) => list.FindAllDescendants()
        .Select(e => Safe(() => e.Name))
        .FirstOrDefault(name => name.StartsWith("正文第 ", StringComparison.Ordinal) && name.EndsWith(" 段", StringComparison.Ordinal)) ?? "";

    private static string PreviewNotice(Window window) => window.FindAllDescendants()
        .Select(e => Safe(() => e.Name))
        .FirstOrDefault(name => name.StartsWith("当前位置附近无可靠匹配节点", StringComparison.Ordinal)) ?? "";

    private static FlaUI.Core.AutomationElements.Scrolling.VerticalScrollBar ScrollBar(ListBox list) =>
        (list.FindFirstDescendant(cf => cf.ByAutomationId("PART_VerticalScrollBar"))
         ?? throw new InvalidOperationException("Preview scrollbar was not found.")).AsVerticalScrollBar();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static bool WaitForExit(int processId, TimeSpan timeout)
    {
        try { using var process = Process.GetProcessById(processId); return process.WaitForExit(timeout); }
        catch (ArgumentException) { return true; }
    }

    private static string Safe(Func<string?> get)
    {
        try { return get() ?? ""; } catch { return ""; }
    }

    private static bool Safe(Func<bool> get)
    {
        try { return get(); } catch { return false; }
    }

    private static void Emit(Report report) => Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
}
