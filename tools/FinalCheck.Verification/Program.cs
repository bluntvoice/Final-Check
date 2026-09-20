using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Comparison;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Management;
using FinalCheck.Data;
using FinalCheck.Documents;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Verification;

public static class Program
{
    private sealed record CheckResult(string Name, string Status, string Stage, string? Reason, long DurationMs, string? Metric);
    private sealed record Report(bool Success, string RunId, string? DataRoot, bool Retained, long DurationMs, IReadOnlyList<CheckResult> Checks);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        var json = args.Contains("json", StringComparer.OrdinalIgnoreCase);
        var valid = args.Length > 0 && args[0] == "verify";
        for (var i = 1; valid && i < args.Length; i++)
        {
            if (args[i] == "--keep") continue;
            if (args[i] == "--format" && i + 1 < args.Length && args[i + 1] is "json" or "text") { i++; continue; }
            valid = false;
        }
        if (!valid)
        {
            Emit(new(false, "", null, false, 0, [new("arguments", "fail", "parse", "Usage: verify [--format json|text] [--keep]", 0, null)]), json);
            return 2;
        }

        var runId = Guid.NewGuid().ToString("N");
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FinalCheckVerification"));
        var runRoot = Path.GetFullPath(Path.Combine(parent, runId));
        var dataRoot = Path.Combine(runRoot, "Data");
        var fixtureRoot = Path.Combine(runRoot, "Fixtures");
        var checks = new List<CheckResult>();
        var overall = Stopwatch.StartNew();
        var keep = args.Contains("--keep", StringComparer.Ordinal);
        var initialized = await Run(checks, "isolation", "temporary-root", async () =>
        {
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(fixtureRoot);
            await File.WriteAllTextAsync(Path.Combine(runRoot, ".verification-owner"), runId);
            return "ownedTemporaryRoot=true";
        });
        if (!initialized)
        {
            Emit(new(false, runId, dataRoot, Directory.Exists(runRoot), overall.ElapsedMilliseconds, checks), json);
            return 1;
        }
        var databasePath = Path.Combine(dataRoot, "finalcheck.db");
        var baselinePath = Path.Combine(fixtureRoot, "baseline.docx");
        var currentPath = Path.Combine(fixtureRoot, "current.docx");
        DocumentSnapshot? baseline = null;
        DocumentSnapshot? current = null;
        ComparisonResult? comparison = null;
        string? baselineHash = null;
        string? currentHash = null;

        var databaseOk = await Run(checks, "database", "sqlite-migration-and-integrity", async () =>
        {
            await using (var context = Open(databasePath))
            {
                await context.Database.MigrateAsync();
                if ((await context.Database.GetPendingMigrationsAsync()).Any()) throw new InvalidDataException("Pending database migrations remain.");
            }
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check";
            var integrity = (string?)await command.ExecuteScalarAsync();
            if (integrity != "ok") throw new InvalidDataException($"SQLite integrity_check: {integrity}");
            command.CommandText = "PRAGMA foreign_key_check";
            if (await command.ExecuteScalarAsync() is not null) throw new InvalidDataException("SQLite foreign_key_check returned violations.");
            return $"schema={FinalCheckDbContext.DatabaseSchemaVersion}; integrity=ok";
        });

        var docxOk = await Run(checks, "docx", "openxml-parse", async () =>
        {
            WriteFixture(baselinePath, 30);
            WriteFixture(currentPath, 60);
            baselineHash = Hash(baselinePath);
            currentHash = Hash(currentPath);
            var parser = new OpenXmlDocumentParser();
            baseline = await parser.ParseFileAsync(baselinePath);
            current = await parser.ParseFileAsync(currentPath);
            if (baseline.ParseStatus != DocumentParseStatus.Complete || current.ParseStatus != DocumentParseStatus.Complete ||
                baseline.Paragraphs.Count != 10 || current.Paragraphs.Count != 10)
                throw new InvalidDataException("Generated DOCX did not parse completely with ten paragraphs per side.");
            if (!baseline.Paragraphs.Any(p => p.DisplayText.Contains("30日", StringComparison.Ordinal)) ||
                !current.Paragraphs.Any(p => p.DisplayText.Contains("60日", StringComparison.Ordinal)))
                throw new InvalidDataException("Expected fixture text is missing from parsed snapshots.");
            return $"baselineParagraphs={baseline.Paragraphs.Count}; currentParagraphs={current.Paragraphs.Count}";
        });

        if (docxOk)
        {
            await Run(checks, "comparison", "engine", () =>
            {
                var text = new TokenTextDiffService(new MixedLanguageTextTokenizer());
                var format = new EffectiveFormatDiffService();
                var engine = new BasicComparisonEngine(new MultiSignalParagraphMatcher(), text, new ParagraphMoveDetector(),
                    format, new TableComparisonService(text, format), new SnapshotAnnotationIntegrationService(), new RuleBasedChangeGroupingService());
                comparison = engine.Compare(baseline!, current!);
                if (comparison.Statistics.TotalChanges == 0 ||
                    !comparison.Changes.Any(change => change.BaselineText.Contains("30日", StringComparison.Ordinal) &&
                        change.CurrentText.Contains("60日", StringComparison.Ordinal)))
                    throw new InvalidDataException("Expected 30-to-60 comparison fact is missing.");
                return Task.FromResult($"changes={comparison.Statistics.TotalChanges}");
            });
        }
        else checks.Add(new("comparison", "skip", "engine", "DOCX parse failed.", 0, null));

        if (databaseOk && comparison is not null)
        {
            await Run(checks, "persistence", "snapshot-and-comparison-reopen", async () =>
            {
                Guid baselineId, currentId, comparisonId;
                await using (var context = Open(databasePath))
                {
                    var snapshots = new DocumentSnapshotStore(context, new JsonDocumentSnapshotSerializer());
                    baselineId = await snapshots.SaveAsync(baseline!);
                    currentId = await snapshots.SaveAsync(current!);
                    comparisonId = await new ComparisonResultStore(context, new JsonComparisonResultSerializer()).SaveAsync(comparison);
                }
                await using (var reopened = Open(databasePath))
                {
                    var snapshots = new DocumentSnapshotStore(reopened, new JsonDocumentSnapshotSerializer());
                    var restored = await snapshots.LoadAsync(currentId);
                    var storedComparison = await new ComparisonResultStore(reopened, new JsonComparisonResultSerializer()).LoadAsync(comparisonId);
                    if (await snapshots.LoadAsync(baselineId) is null || restored?.Paragraphs.Count != 10 ||
                        storedComparison?.Statistics.TotalChanges != comparison.Statistics.TotalChanges)
                        throw new InvalidDataException("Reopened snapshot or comparison differs from saved facts.");
                }
                return $"snapshots=2; comparisonChanges={comparison.Statistics.TotalChanges}";
            });
        }
        else checks.Add(new("persistence", "skip", "snapshot-and-comparison-reopen", "Database or comparison failed.", 0, null));

        if (databaseOk && docxOk)
        {
            await Run(checks, "project-persistence", "project-versions-reopen", async () =>
            {
                Guid projectId;
                await using (var context = Open(databasePath))
                {
                    projectId = (await new ProjectStore(context).SaveAsync(null,
                        new("Verification Project", "Fixture Counterparty", "Contract", null, null, "", [], ""))).ProjectId;
                    var store = new ContractVersionStore(context, new DocumentSnapshotStore(context, new JsonDocumentSnapshotSerializer()));
                    await store.ImportAsync(projectId,
                    [
                        new(new(baselinePath, ContractVersionRole.Own, 1, ""), FileIdentity(baselinePath), baseline!),
                        new(new(currentPath, ContractVersionRole.Counterparty, 1, ""), FileIdentity(currentPath), current!)
                    ]);
                }
                await using var reopened = Open(databasePath);
                var project = await new ProjectStore(reopened).GetAsync(projectId);
                var versions = await new ContractVersionStore(reopened,
                    new DocumentSnapshotStore(reopened, new JsonDocumentSnapshotSerializer())).ListAsync(projectId);
                if (project.ProjectName != "Verification Project" || versions.Count != 2 ||
                    !versions.Any(version => version.Role == ContractVersionRole.Own) ||
                    !versions.Any(version => version.Role == ContractVersionRole.Counterparty))
                    throw new InvalidDataException("Reopened project or explicit-role versions differ from saved facts.");
                return "projects=1; versions=2; roles=own,counterparty";
            });
        }
        else checks.Add(new("project-persistence", "skip", "project-versions-reopen", "Database or DOCX parse failed.", 0, null));

        if (docxOk)
        {
            await Run(checks, "original-safety", "sha256", () =>
            {
                if (baselineHash != Hash(baselinePath) || currentHash != Hash(currentPath))
                    throw new InvalidDataException("An original fixture DOCX changed during verification.");
                if (Directory.EnumerateFiles(dataRoot, "*.docx", SearchOption.AllDirectories).Any())
                    throw new InvalidDataException("An original fixture DOCX was copied into DataRoot.");
                return Task.FromResult("sourceSha256Unchanged=true; originalsOutsideDataRoot=true");
            });
        }
        else checks.Add(new("original-safety", "skip", "sha256", "DOCX fixture creation failed.", 0, null));

        var success = checks.All(check => check.Status == "pass");
        if (success && !keep)
        {
            success = await Run(checks, "cleanup", "temporary-root", () =>
            {
                DeleteOwnedTemporaryRun(parent, runRoot, runId);
                return Task.FromResult("removed=true");
            });
        }
        overall.Stop();
        Emit(new(success, runId, dataRoot, Directory.Exists(runRoot), overall.ElapsedMilliseconds, checks), json);
        return success ? 0 : 1;
    }

    private static async Task<bool> Run(List<CheckResult> checks, string name, string stage, Func<Task<string>> action)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var metric = await action();
            checks.Add(new(name, "pass", stage, null, watch.ElapsedMilliseconds, metric));
            return true;
        }
        catch (Exception error)
        {
            checks.Add(new(name, "fail", stage, $"{error.GetType().Name}: {error.Message}", watch.ElapsedMilliseconds, null));
            return false;
        }
    }

    private static FinalCheckDbContext Open(string path) => new(new DbContextOptionsBuilder<FinalCheckDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()).Options);

    private static void WriteFixture(string path, int days)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        main.Document = new Document(new Body());
        for (var i = 0; i < 10; i++)
            main.Document.Body!.Append(new Paragraph(new Run(new Text($"第{i + 1}条 付款期限为{days}日，双方按合同约定履行。"))));
        main.Document.Save();
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static FinalCheck.Core.Comparisons.ComparisonFile FileIdentity(string path)
    {
        var file = new FileInfo(path);
        return new(path, file.Name, file.Length, file.LastWriteTimeUtc, Hash(path));
    }

    private static void DeleteOwnedTemporaryRun(string parent, string runRoot, string runId)
    {
        if (Path.GetRelativePath(parent, runRoot) != runId ||
            !string.Equals(Path.GetFullPath(Path.GetDirectoryName(runRoot)!), parent, StringComparison.OrdinalIgnoreCase) ||
            File.ReadAllText(Path.Combine(runRoot, ".verification-owner")) != runId)
            throw new InvalidOperationException("Temporary run ownership or target path did not validate; nothing was deleted.");
        Directory.Delete(runRoot, recursive: true);
    }

    private static void Emit(Report report, bool json)
    {
        if (json) { Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions)); return; }
        Console.WriteLine("Final Check Verification");
        foreach (var check in report.Checks)
            Console.WriteLine($"{check.Name,-22} {check.Status.ToUpperInvariant(),-5} {check.Metric ?? check.Reason}");
        Console.WriteLine($"Overall                {(report.Success ? "PASS" : "FAIL")}");
        if (report.Retained) Console.WriteLine($"Retained isolation root: {report.DataRoot}");
    }
}
