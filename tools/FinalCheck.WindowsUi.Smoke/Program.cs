using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace FinalCheck.WindowsUi.Smoke;

public static class Program
{
    private sealed record UiReport(bool Success, string Stage, string? Reason, int? ProcessId, string? WindowTitle,
        string? NavigationId, string? ControlId, bool? StartEnabled, long DurationMs, bool TemporaryDataRetained);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--app" || !Path.IsPathFullyQualified(args[1]))
        {
            Emit(new(false, "arguments", "Usage: --app <absolute path to isolated Debug FinalCheck.Desktop.exe>", null, null, null, null, null, 0, false));
            return 2;
        }
        var appPath = Path.GetFullPath(args[1]);
        if (!OperatingSystem.IsWindows() || !File.Exists(appPath) ||
            !string.Equals(Path.GetFileName(appPath), "FinalCheck.Desktop.exe", StringComparison.OrdinalIgnoreCase) ||
            !appPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            Emit(new(false, "arguments", "The spike only launches an existing Debug FinalCheck.Desktop.exe with an isolated data root.", null, null, null, null, null, 0, false));
            return 2;
        }

        var runId = Guid.NewGuid().ToString("N");
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FinalCheckVerification"));
        var root = Path.GetFullPath(Path.Combine(parent, "UiAutomation-" + runId));
        var dataRoot = Path.Combine(root, "Data");
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(root, ".verification-owner"), runId);
        var watch = Stopwatch.StartNew();
        var stage = "launch";
        int? processId = null;
        string? title = null;
        string? navigationId = null;
        string? controlId = null;
        bool? startEnabled = null;
        string? error = null;
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
            stage = "automation-tree";
            var navigation = window.FindFirstDescendant(cf => cf.ByAutomationId("MainQuickCompare"))?.AsButton()
                ?? throw new InvalidOperationException("MainQuickCompare AutomationId was not found.");
            navigationId = navigation.AutomationId;
            stage = "invoke-navigation";
            navigation.Invoke();
            stage = "read-page-state";
            var ready = Stopwatch.StartNew();
            AutomationElement? startControl = null;
            while (ready.Elapsed < TimeSpan.FromSeconds(5) && startControl is null)
            {
                startControl = window.FindFirstDescendant(cf => cf.ByAutomationId("ComparisonStart"));
                if (startControl is null) Thread.Sleep(100);
            }
            if (startControl is null) throw new InvalidOperationException("ComparisonStart AutomationId was not found after navigation.");
            controlId = startControl.AutomationId;
            startEnabled = startControl.IsEnabled;
            if (startEnabled.Value) throw new InvalidDataException("ComparisonStart is unexpectedly enabled without DOCX inputs.");
            stage = "close";
            app.Close();
            if (!WaitForExit(processId.Value, TimeSpan.FromSeconds(8)))
                throw new TimeoutException("The isolated application did not exit after normal close.");
            stage = "cleanup";
            DeleteOwnedTemporaryRun(parent, root, runId);
            stage = "complete";
        }
        catch (Exception exception)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
        }
        finally
        {
            if (app is not null)
            {
                try { if (!app.HasExited) app.Close(); } catch { /* Preserve the primary failure and isolation root. */ }
                app.Dispose();
            }
        }
        watch.Stop();
        var success = error is null;
        Emit(new(success, stage, error, processId, title, navigationId, controlId, startEnabled,
            watch.ElapsedMilliseconds, Directory.Exists(root)));
        return success ? 0 : 1;
    }

    private static bool WaitForExit(int processId, TimeSpan timeout)
    {
        try { using var process = Process.GetProcessById(processId); return process.WaitForExit(timeout); }
        catch (ArgumentException) { return true; }
    }

    private static void DeleteOwnedTemporaryRun(string parent, string root, string runId)
    {
        if (Path.GetRelativePath(parent, root) != "UiAutomation-" + runId ||
            !string.Equals(Path.GetFullPath(Path.GetDirectoryName(root)!), parent, StringComparison.OrdinalIgnoreCase) ||
            File.ReadAllText(Path.Combine(root, ".verification-owner")) != runId)
            throw new InvalidOperationException("Temporary root ownership did not validate; nothing was deleted.");
        Directory.Delete(root, recursive: true);
    }

    private static void Emit(UiReport report) => Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
}
