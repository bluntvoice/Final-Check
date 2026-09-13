using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Comparison;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Formatting;
using FinalCheck.Data.Entities;
using FinalCheck.Documents;
using FinalCheck.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit.Abstractions;

namespace FinalCheck.Data.Tests;

public sealed class FormatRestoreWorkflowTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CreateUpdateAndUndoUseOneWorkingFileAndKeepOriginalHistory()
    {
        await using var env = await Environment.CreateAsync();
        var original = File.ReadAllBytes(env.Original);
        var first = await env.Service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync());
        Assert.Equal(FormatRestoreResultStatus.Completed, first.Status);
        var path = first.WorkingCopy!.WorkingPath;
        Assert.Equal("修改后合同60日", first.WorkingCopy.Snapshot.Paragraphs[0].DisplayText);
        Assert.True(first.WorkingCopy.Snapshot.Paragraphs[0].Runs[0].EffectiveFormatting.Bold);
        Assert.Equal(2, first.Operation!.Mutations.Count);
        var noChanges = await env.Service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync(path));
        Assert.Equal(FormatRestoreResultStatus.NoChanges, noChanges.Status);
        var second = await env.Service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync(path, reverse: true));
        Assert.Equal(FormatRestoreResultStatus.Completed, second.Status);
        Assert.Equal(path, second.WorkingCopy!.WorkingPath);
        var undo = await env.Service.UndoLastAsync(env.Version);
        Assert.Equal(FormatRestoreResultStatus.Completed, undo.Status);
        Assert.Equal(path, undo.WorkingCopy!.WorkingPath);
        Assert.True(undo.WorkingCopy.Snapshot.Paragraphs[0].Runs[0].EffectiveFormatting.Bold);
        Assert.Equal(original, File.ReadAllBytes(env.Original));
        Assert.Equal(3, await env.Context.FormatRestoreOperations.CountAsync());
        Assert.Equal(1, await env.Context.RestoredWorkingCopies.CountAsync());
        Assert.Equal(FormatRestoreResultStatus.Failed, (await env.Service.UndoLastAsync(env.Version)).Status);
        Assert.Equal("Undone", (await env.Context.FormatRestoreOperations.SingleAsync(o => o.Id == second.Operation!.OperationId)).Status);
        Assert.False(env.Context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task ExternalEditingRejectsRestoreAndUndoThenPreserveAppendsNewComparison()
    {
        await using var env = await Environment.CreateAsync();
        var first = await env.Service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync());
        var path = first.WorkingCopy!.WorkingPath;
        var oldPlan = await env.PlanAsync(path);
        using (var document = WordprocessingDocument.Open(path, true))
        {
            document.MainDocumentPart!.Document!.Descendants<Text>().Single().Text = "Word外部新增文字";
            document.MainDocumentPart.Document.Save();
        }
        var externallyEdited = File.ReadAllBytes(path);
        Assert.Equal(FormatRestoreResultStatus.WorkingCopyExternallyModified, (await env.Service.ExecuteAsync(env.Version, env.Original, oldPlan)).Status);
        Assert.Equal(FormatRestoreResultStatus.WorkingCopyExternallyModified, (await env.Service.UndoLastAsync(env.Version)).Status);
        var preserved = await env.Service.PreserveExternalChangesAsync(env.Version, await env.Parser.ParseFileAsync(env.Baseline));
        Assert.Equal(FormatRestoreResultStatus.Completed, preserved.Status);
        Assert.Equal("Word外部新增文字", preserved.WorkingCopy!.Snapshot.Paragraphs[0].DisplayText);
        Assert.Equal(externallyEdited, File.ReadAllBytes(path));
        Assert.Equal(1, await env.Context.DocumentSnapshots.CountAsync());
        Assert.Equal(1, await env.Context.ComparisonResults.CountAsync());
        Assert.Equal(FormatRestoreResultStatus.Failed, (await env.Service.UndoLastAsync(env.Version)).Status);
    }

    [Fact]
    public async Task ExplicitRegeneratePreservesExternalBackupAndOriginal()
    {
        await using var env = await Environment.CreateAsync();
        var first = await env.Service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync());
        var path = first.WorkingCopy!.WorkingPath;
        using (var document = WordprocessingDocument.Open(path, true))
        {
            document.MainDocumentPart!.Document!.Descendants<Text>().Single().Text = "外部编辑";
            document.MainDocumentPart.Document.Save();
        }
        var externalHash = Hash(File.ReadAllBytes(path));
        var regenerated = await env.Service.RegenerateAsync(env.Version);
        Assert.Equal(FormatRestoreResultStatus.Completed, regenerated.Status);
        Assert.Equal(File.ReadAllBytes(env.Original), File.ReadAllBytes(path));
        Assert.Equal(externalHash, Hash(File.ReadAllBytes(regenerated.Operation!.BackupPath)));
    }

    [Fact]
    public async Task MigrationFromSchemaTwoPreservesBothHistoricalPayloads()
    {
        await using var env = await Environment.CreateAsync(migrate: false);
        var migrator = env.Context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260910143000_AddComparisonResults");
        var snapshotId = Guid.NewGuid();
        var comparisonId = Guid.NewGuid();
        env.Context.DocumentSnapshots.Add(new StoredDocumentSnapshot { Id = snapshotId, SnapshotSchemaVersion = 2, Payload = [1, 2, 3], CreatedAtUtc = DateTime.UtcNow });
        env.Context.ComparisonResults.Add(new StoredComparisonResult { Id = comparisonId, ComparisonSchemaVersion = 1, Payload = [4, 5, 6], CreatedAtUtc = DateTime.UtcNow });
        await env.Context.SaveChangesAsync(); env.Context.ChangeTracker.Clear();
        await migrator.MigrateAsync();
        Assert.Equal(new byte[] { 1, 2, 3 }, (await env.Context.DocumentSnapshots.SingleAsync()).Payload);
        Assert.Equal(new byte[] { 4, 5, 6 }, (await env.Context.ComparisonResults.SingleAsync()).Payload);
        Assert.Equal(FinalCheckDbContext.DatabaseSchemaVersion, (await env.Context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task CancellationAndFailedValidationNeverPublishWorkingFile()
    {
        await using var env = await Environment.CreateAsync();
        var plan = await env.PlanAsync();
        Assert.Equal(FormatRestoreResultStatus.Cancelled, (await env.Service.ExecuteAsync(env.Version, env.Original, plan, cancellationToken: new(true))).Status);
        Assert.Equal(FormatRestoreResultStatus.Failed, (await env.Service.ExecuteAsync(env.Version, env.Original, plan with { SourceSha256 = new string('a', 64) })).Status);
        Assert.Equal(0, await env.Context.RestoredWorkingCopies.CountAsync());
        Assert.Empty(Directory.GetFiles(env.Root, "restored.docx", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DatabaseFailureRollsBackOldWorkingFileWithoutLosingData()
    {
        await using var env = await Environment.CreateAsync();
        var first = await env.Service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync());
        var path = first.WorkingCopy!.WorkingPath;
        var original = File.ReadAllBytes(env.Original);
        var before = File.ReadAllBytes(path);
        var failing = new FaultingStore(env.Store);
        var service = env.NewService(failing);
        var result = await service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync(path, reverse: true));
        Assert.Equal(FormatRestoreResultStatus.Failed, result.Status);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(env.Original));
        Assert.Empty(await env.Store.LoadPendingAsync(env.Version));
    }

    [Fact]
    public async Task PreparedJournalCanFinishAfterSimulatedCrashWithoutRepeatingRestore()
    {
        await using var env = await Environment.CreateAsync();
        var plan = await env.PlanAsync();
        using var source = File.OpenRead(env.Original);
        var rendered = await new OpenXmlFormatRestoreRenderer(env.Parser).RenderAsync(source, plan);
        var id = Guid.NewGuid();
        var directory = Path.Combine(env.Root, "WorkingCopies", env.Version.ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "restored.docx");
        File.WriteAllBytes(path, rendered.DocumentBytes);
        var copy = new RestoredWorkingCopy(env.Version, env.Original, plan.SourceSha256, path, rendered.Snapshot.Metadata.Sha256, rendered.Snapshot, DateTimeOffset.UtcNow, id);
        var operation = new FormatRestoreOperation(1, id, env.Version, FormatRestoreOperationKind.Restore, FormatRestoreOperationStatus.Prepared,
            plan, new(), DateTimeOffset.UtcNow, null, null, copy.Sha256, rendered.Mutations, [], copy,
            Path.Combine(directory, $"candidate-{id:N}.docx"), Path.Combine(directory, $"previous-{id:N}.docx"));
        await env.Store.SavePreparedAsync(operation);
        var result = await env.Service.RecoverAsync(env.Version);
        Assert.Equal(FormatRestoreResultStatus.Completed, result.Status);
        Assert.Equal(1, await env.Context.FormatRestoreOperations.CountAsync());
        Assert.Empty(await env.Store.LoadPendingAsync(env.Version));
    }

    [Fact]
    public async Task UnknownPersistenceSchemaIsRejected()
    {
        await using var env = await Environment.CreateAsync();
        var first = await env.Service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync());
        var row = await env.Context.FormatRestoreOperations.SingleAsync();
        row.SchemaVersion = 99; await env.Context.SaveChangesAsync(); env.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => env.Store.LoadLastOperationAsync(env.Version));
        Assert.Equal(first.WorkingCopy!.Sha256, Hash(File.ReadAllBytes(first.WorkingCopy.WorkingPath)));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(300)]
    public async Task SmallAndMediumRecordPlanExecutionReparseAllocationsAndWorkingSize(int paragraphs)
    {
        await using var env = await Environment.CreateAsync(paragraphs: paragraphs);
        var baseline = await env.Parser.ParseFileAsync(env.Baseline);
        var current = await env.Parser.ParseFileAsync(env.Original);
        var comparison = Environment.Engine().Compare(baseline, current);
        var warmPlan = new SnapshotFormatRestorePlanner().Generate(baseline, current, comparison);
        Assert.Equal(FormatRestoreResultStatus.Completed, (await env.Service.ExecuteAsync(Guid.NewGuid(), env.Original, warmPlan)).Status);
        var allocations = GC.GetTotalAllocatedBytes(true);
        var sw = Stopwatch.StartNew();
        var plan = new SnapshotFormatRestorePlanner().Generate(baseline, current, comparison); var planTime = sw.Elapsed.TotalMilliseconds;
        var progress = new Timings(); sw.Restart();
        var result = await env.Service.ExecuteAsync(env.Version, env.Original, plan, progress: progress);
        var executionTime = sw.Elapsed.TotalMilliseconds;
        Assert.Equal(FormatRestoreResultStatus.Completed, result.Status);
        var allocated = GC.GetTotalAllocatedBytes(true) - allocations;
        output.WriteLine($"FormatRestore paragraphs={paragraphs}; plan={planTime:F3}ms; execute={executionTime:F3}ms; reparseStages={progress.ReparseMilliseconds:F3}ms; allocations={allocated}B; working={new FileInfo(result.WorkingCopy!.WorkingPath).Length}B");
        Assert.True(executionTime < 30000, "Synthetic restore exceeded regression threshold, not a formal SLA.");
    }

    [Fact]
    public async Task CancellationDuringCandidateWriteKeepsOldWorkingFileAndOriginal()
    {
        await using var env = await Environment.CreateAsync();
        var first = await env.Service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync());
        var path = first.WorkingCopy!.WorkingPath;
        var before = File.ReadAllBytes(path); var original = File.ReadAllBytes(env.Original);
        using var cancellation = new CancellationTokenSource();
        var progress = new CandidateCancellation(cancellation);
        var result = await env.Service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync(path, reverse: true), progress: progress, cancellationToken: cancellation.Token);
        Assert.Equal(FormatRestoreResultStatus.Cancelled, result.Status);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(env.Original));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "candidate-*.docx"));
        Assert.Empty(await env.Store.LoadPendingAsync(env.Version));
    }

    [Fact]
    public async Task FileLockFailureDoesNotDamageWorkingCopy()
    {
        await using var env = await Environment.CreateAsync();
        var first = await env.Service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync());
        var path = first.WorkingCopy!.WorkingPath;
        var plan = await env.PlanAsync(path, reverse: true); var before = File.ReadAllBytes(path);
        // The operation lock is portable/cooperative; no assumptions about Unix advisory document locks.
        using (var locked = new FileStream(Path.Combine(Path.GetDirectoryName(path)!, "operation.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(FormatRestoreResultStatus.Failed, (await env.Service.ExecuteAsync(env.Version, env.Original, plan)).Status);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(1, await env.Context.FormatRestoreOperations.CountAsync());
    }

    [Fact]
    public async Task ObserverFailureAfterCommitCannotRollBackSuccessfulRestore()
    {
        await using var env = await Environment.CreateAsync();
        var result = await env.Service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync(), progress: new FinalObserverFailure());
        Assert.Equal(FormatRestoreResultStatus.Completed, result.Status);
        Assert.Equal(result.WorkingCopy!.Sha256, Hash(File.ReadAllBytes(result.WorkingCopy.WorkingPath)));
        Assert.Equal(result.WorkingCopy.Sha256, (await env.Store.LoadWorkingCopyAsync(env.Version))!.Sha256);
    }

    private sealed class CandidateCancellation(CancellationTokenSource cancellation) : IProgress<FormatRestoreProgress>
    {
        private int _saving;
        public void Report(FormatRestoreProgress value)
        {
            if (value.Stage == FormatRestoreStage.Saving && ++_saving == 2) cancellation.Cancel();
        }
    }

    [Fact]
    public async Task DatabaseExceptionAfterCommitMustNotRollbackCommittedFile()
    {
        await using var env = await Environment.CreateAsync();
        var result = await env.NewService(new FaultingStore(env.Store, afterCommit: true)).ExecuteAsync(env.Version, env.Original, await env.PlanAsync());
        Assert.Equal(FormatRestoreResultStatus.Completed, result.Status);
        Assert.Equal(result.WorkingCopy!.Sha256, Hash(File.ReadAllBytes(result.WorkingCopy.WorkingPath)));
        Assert.Equal("Completed", (await env.Context.FormatRestoreOperations.SingleAsync()).Status);
        Assert.Empty(await env.Store.LoadPendingAsync(env.Version));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnpromotedJournalFailsSafelyAndUnknownHashNeedsReview(bool externallyEdited)
    {
        await using var env = await Environment.CreateAsync();
        var first = await env.Service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync());
        var copy = first.WorkingCopy!; var plan = await env.PlanAsync(copy.WorkingPath, reverse: true);
        FormatRestoreRenderResult rendered;
        using (var input = File.OpenRead(copy.WorkingPath)) rendered = await new OpenXmlFormatRestoreRenderer(env.Parser).RenderAsync(input, plan);
        var id = Guid.NewGuid(); var directory = Path.GetDirectoryName(copy.WorkingPath)!;
        var proposed = copy with { Sha256 = rendered.Snapshot.Metadata.Sha256, Snapshot = rendered.Snapshot, LastOperationId = id };
        var pending = new FormatRestoreOperation(1, id, env.Version, FormatRestoreOperationKind.Restore, FormatRestoreOperationStatus.Prepared,
            plan, new(), DateTimeOffset.UtcNow, null, copy.Sha256, proposed.Sha256, rendered.Mutations, [], proposed,
            Path.Combine(directory, $"candidate-{id:N}.docx"), Path.Combine(directory, $"previous-{id:N}.docx"), PreviousMetadataSha256: copy.Sha256);
        await env.Store.SavePreparedAsync(pending);
        if (externallyEdited)
        {
            using var package = WordprocessingDocument.Open(copy.WorkingPath, true);
            package.MainDocumentPart!.Document!.Descendants<Text>().Single().Text = "恢复期间外部修改";
            package.MainDocumentPart.Document.Save();
        }
        var beforeRecovery = File.ReadAllBytes(copy.WorkingPath);
        var result = await env.Service.RecoverAsync(env.Version);
        Assert.Equal(externallyEdited ? FormatRestoreResultStatus.RecoveryNeedsReview : FormatRestoreResultStatus.NoChanges, result.Status);
        Assert.Equal(beforeRecovery, File.ReadAllBytes(copy.WorkingPath));
        Assert.Equal(externallyEdited ? 1 : 0, (await env.Store.LoadPendingAsync(env.Version)).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrSnapshotMismatchedCandidateCannotReplaceOldWorkingCopyOrCompleteHistory(bool validPackage)
    {
        await using var env = await Environment.CreateAsync();
        var first = await env.Service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync());
        var copy = first.WorkingCopy!; var before = File.ReadAllBytes(copy.WorkingPath);
        var service = env.NewService(env.Store, new CorruptCandidateRenderer(new OpenXmlFormatRestoreRenderer(env.Parser), validPackage));
        var result = await service.ExecuteAsync(env.Version, env.Original, await env.PlanAsync(copy.WorkingPath, reverse: true));
        Assert.Equal(FormatRestoreResultStatus.Failed, result.Status);
        Assert.Equal(before, File.ReadAllBytes(copy.WorkingPath));
        Assert.Equal(1, await env.Context.FormatRestoreOperations.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealComparisonMappingRestoresOnlyTrustedChangedContractText(bool clauseHeading)
    {
        await using var env = await Environment.CreateAsync();
        foreach (var (path, text) in new[]
        {
            (env.Baseline, "付款期限为30日，双方应当按照合同约定及时履行义务。"),
            (env.Original, "付款期限为60日，双方应当按照合同约定及时履行义务。"),
        })
        {
            using var package = WordprocessingDocument.Open(path, true);
            package.MainDocumentPart!.Document!.Descendants<Text>().Single().Text = (clauseHeading ? "第1条 " : "") + text;
            package.MainDocumentPart.Document.Save();
        }
        var original = File.ReadAllBytes(env.Original);
        var plan = await env.PlanAsync();
        var result = await env.Service.ExecuteAsync(env.Version, env.Original, plan);
        if (!clauseHeading)
        {
            Assert.Equal(FormatRestoreResultStatus.NoChanges, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Code == "LowConfidenceMapping");
            Assert.All(plan.RestoreItems, i => Assert.Equal(FormatRestoreEligibility.NeedsReview, i.Eligibility));
            Assert.Equal(original, File.ReadAllBytes(env.Original));
            return;
        }
        Assert.True(result.Status == FormatRestoreResultStatus.Completed, JsonSerializer.Serialize(new { plan.Diagnostics, plan.RestoreItems, result.Status }));
        Assert.Contains("60日", result.WorkingCopy!.Snapshot.Paragraphs[0].DisplayText, StringComparison.Ordinal);
        Assert.DoesNotContain("30日", result.WorkingCopy.Snapshot.Paragraphs[0].DisplayText, StringComparison.Ordinal);
        Assert.True(result.WorkingCopy.Snapshot.Paragraphs[0].Runs[0].EffectiveFormatting.Bold);
        Assert.Equal(original, File.ReadAllBytes(env.Original));
    }

    private sealed class CorruptCandidateRenderer(IFormatRestoreRenderer inner, bool validPackage) : IFormatRestoreRenderer
    {
        public async ValueTask<FormatRestoreRenderResult> RenderAsync(Stream source, FormatRestorePlan plan, FormatRestoreScope? scope = null,
            IProgress<FormatRestoreProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var rendered = await inner.RenderAsync(source, plan, scope, progress, cancellationToken);
            if (!validPackage) return rendered with { DocumentBytes = [1, 2, 3] };
            using var buffer = new MemoryStream(); buffer.Write(rendered.DocumentBytes); buffer.Position = 0;
            using (var package = WordprocessingDocument.Open(buffer, true))
            {
                package.MainDocumentPart!.Document!.Descendants<Text>().Single().Text = "有效候选文件但 Snapshot 不一致";
                package.MainDocumentPart.Document.Save();
            }
            return rendered with { DocumentBytes = buffer.ToArray() };
        }
        public ValueTask<FormatRestoreRenderResult> RevertAsync(Stream source, string expectedSha256, IReadOnlyList<FormatRestoreMutation> mutations, CancellationToken cancellationToken = default) =>
            inner.RevertAsync(source, expectedSha256, mutations, cancellationToken);
    }
    private sealed class FinalObserverFailure : IProgress<FormatRestoreProgress>
    {
        private int _completed;
        public void Report(FormatRestoreProgress value)
        {
            if (value.Stage == FormatRestoreStage.Completed && ++_completed == 2) throw new InvalidOperationException("Injected observer error after publication.");
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed class Timings : IProgress<FormatRestoreProgress>
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private FormatRestoreStage? _stage;
        private double _last;
        public double ReparseMilliseconds { get; private set; }
        public void Report(FormatRestoreProgress value)
        {
            var now = _watch.Elapsed.TotalMilliseconds;
            if (_stage == FormatRestoreStage.Reparsing) ReparseMilliseconds += now - _last;
            _last = now; _stage = value.Stage;
        }
    }

    private sealed class FaultingStore(IFormatRestoreStore inner, bool afterCommit = false) : IFormatRestoreStore
    {
        public Task<RestoredWorkingCopy?> LoadWorkingCopyAsync(Guid id, CancellationToken token = default) => inner.LoadWorkingCopyAsync(id, token);
        public Task<FormatRestoreOperation?> LoadLastOperationAsync(Guid id, CancellationToken token = default) => inner.LoadLastOperationAsync(id, token);
        public Task<IReadOnlyList<FormatRestoreOperation>> LoadPendingAsync(Guid id, CancellationToken token = default) => inner.LoadPendingAsync(id, token);
        public Task SavePreparedAsync(FormatRestoreOperation op, CancellationToken token = default) => inner.SavePreparedAsync(op, token);
        public async Task CompleteAsync(FormatRestoreOperation op, CancellationToken token = default)
        {
            if (afterCommit) await inner.CompleteAsync(op, token);
            throw new IOException("Injected database completion failure.");
        }
        public Task FailAsync(FormatRestoreOperation op, CancellationToken token = default) => inner.FailAsync(op, token);
    }

    private sealed class Environment : IAsyncDisposable, IAppDataPathProvider
    {
        public string Root { get; } = Path.Combine(PhysicalTemporaryRoot(), "FinalCheck.FormatRestore.Tests", Guid.NewGuid().ToString("N"));
        public string Original => Path.Combine(Root, "current.docx");
        public string Baseline => Path.Combine(Root, "baseline.docx");
        public Guid Version { get; } = Guid.NewGuid();
        public OpenXmlDocumentParser Parser { get; } = new();
        public FinalCheckDbContext Context { get; private set; } = null!;
        public FormatRestoreStore Store { get; private set; } = null!;
        public FormatRestoreWorkingCopyService Service => NewService(Store);
        public string GetAppDataDirectory() => Root;
        public string GetDatabasePath() => Path.Combine(Root, "test.db");
        private static string PhysicalTemporaryRoot()
        {
            // macOS system temporary roots may pass through /var -> /private/var.
            // Resolve the trusted test root, not user-selected managed/source paths.
            var temporary = Path.GetFullPath(Path.GetTempPath());
            var physical = Path.GetPathRoot(temporary)!;
            foreach (var component in temporary[physical.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                physical = Path.Combine(physical, component);
                physical = new DirectoryInfo(physical).ResolveLinkTarget(true)?.FullName ?? physical;
            }
            return physical;
        }
        public static async Task<Environment> CreateAsync(bool migrate = true, int paragraphs = 1)
        {
            var env = new Environment(); Directory.CreateDirectory(env.Root);
            WriteDocument(env.Baseline, true, paragraphs); WriteDocument(env.Original, false, paragraphs);
            env.Context = new(new DbContextOptionsBuilder<FinalCheckDbContext>().UseSqlite($"Data Source={env.GetDatabasePath()};Pooling=False").Options);
            if (migrate) await env.Context.Database.MigrateAsync();
            env.Store = new(env.Context, new JsonDocumentSnapshotSerializer(), new JsonComparisonResultSerializer());
            return env;
        }
        public FormatRestoreWorkingCopyService NewService(IFormatRestoreStore store, IFormatRestoreRenderer? renderer = null) => new(this, Parser, renderer ?? new OpenXmlFormatRestoreRenderer(Parser), store, new WorkingCopyComparisonService(Engine()));
        public async Task<FormatRestorePlan> PlanAsync(string? currentPath = null, bool reverse = false)
        {
            var baseline = await Parser.ParseFileAsync(reverse ? Original : Baseline);
            var current = await Parser.ParseFileAsync(currentPath ?? Original);
            return new SnapshotFormatRestorePlanner().Generate(baseline, current, Engine().Compare(baseline, current));
        }
        public static BasicComparisonEngine Engine()
        {
            var diff = new TokenTextDiffService(new MixedLanguageTextTokenizer()); var format = new EffectiveFormatDiffService();
            return new(new MultiSignalParagraphMatcher(), diff, new ParagraphMoveDetector(), format, new TableComparisonService(diff, format), new SnapshotAnnotationIntegrationService(), new RuleBasedChangeGroupingService());
        }
        private static void WriteDocument(string path, bool formatted, int paragraphs)
        {
            using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(Enumerable.Range(0, paragraphs).Select(i => new Paragraph(
                formatted ? new ParagraphProperties(new Justification { Val = JustificationValues.Center }) : new ParagraphProperties(),
                new Run(formatted ? new RunProperties(new Bold()) : new RunProperties(), new Text(paragraphs == 1 ? "修改后合同60日" : $"第{i}条双方约定付款期限为60日，不改变正文，仅恢复格式。"))))));
            main.Document.Save();
        }
        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            var target = Path.GetFullPath(Root);
            var expectedRoot = Path.GetFullPath(Path.Combine(PhysicalTemporaryRoot(), "FinalCheck.FormatRestore.Tests")) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test cleanup path.");
            Directory.Delete(target, true);
        }
    }
}
