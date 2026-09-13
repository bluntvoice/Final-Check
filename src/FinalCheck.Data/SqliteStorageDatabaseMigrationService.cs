using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Storage;
using FinalCheck.Core.Formatting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data;

public sealed class SqliteStorageDatabaseMigrationService(IDocumentSnapshotSerializer snapshots,
    IComparisonResultSerializer comparisons, IDocumentParser parser) : IStorageDatabaseMigrationService
{
    private static readonly JsonSerializerOptions StrictJson = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public async Task<IStorageDatabaseMigrationSession> OpenSourceAsync(string sourceRoot, CancellationToken cancellationToken = default)
    {
        var database = Path.Combine(sourceRoot, "finalcheck.db");
        await new SqliteDataRootDatabaseInspector().ValidateAsync(database, cancellationToken);
        var guard = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database,
            Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5 }.ToString());
        try
        {
            await guard.OpenAsync(cancellationToken);
            // Empty IMMEDIATE transaction prevents even non-cooperative SQLite writers during the whole copy.
            // Backup reads use a separate connection: do not backup a connection owning a write transaction.
            var transaction = guard.BeginTransaction(deferred: false);
            var session = new Session(sourceRoot, guard, transaction, snapshots, comparisons, parser);
            await session.InitializeAsync(cancellationToken);
            return session;
        }
        catch { await guard.DisposeAsync(); throw; }
    }

    private sealed class Session(string sourceRoot, SqliteConnection guard, SqliteTransaction transaction,
        IDocumentSnapshotSerializer snapshots, IComparisonResultSerializer comparisons, IDocumentParser parser) : IStorageDatabaseMigrationSession
    {
        private Dictionary<string, string> _expected = [];
        public IReadOnlySet<string> OriginalPaths { get; private set; } = new HashSet<string>();
        private static FinalCheckDbContext Open(string root) => new DataRootDbContextFactory(
            new DataRootPaths(new(root, Guid.NewGuid(), 1, 1))).CreateDbContext();
        public async Task InitializeAsync(CancellationToken token)
        {
            await using var context = Open(sourceRoot);
            if (!(await context.Database.GetAppliedMigrationsAsync(token)).SequenceEqual(SqliteDataRootDatabaseInspector.KnownMigrations))
                throw new InvalidDataException("Storage migration does not perform product database upgrades.");
            if (await context.FormatRestoreOperations.AnyAsync(o => o.Status == "Prepared", token))
                throw new InvalidDataException("Prepared restore operations must be recovered before migration.");
            _expected = await FactsAsync(context, sourceRoot, token);
            OriginalPaths = (await context.RestoredWorkingCopies.AsNoTracking().ToArrayAsync(token))
                .Select(row => DecodeCopy(row.Payload).OriginalPath).ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var originals = OriginalPaths.ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var row in await context.ComparisonRecords.AsNoTracking().ToArrayAsync(token))
            {
                var record = ComparisonRecordStore.Decode(row.Payload);
                originals.Add(Path.GetFullPath(record.BaselineFile.Path)); originals.Add(Path.GetFullPath(record.CurrentFile.Path));
            }
            OriginalPaths = originals;
            foreach (var row in await context.TemplateVersions.AsNoTracking().ToArrayAsync(token)) originals.Add(Path.GetFullPath(row.FilePath));
            foreach (var row in await context.ContractVersions.AsNoTracking().ToArrayAsync(token))
            { var version = ContractVersionStore.Map(row); originals.Add(Path.GetFullPath(version.Source.Path)); originals.Add(Path.GetFullPath(version.OriginalSourceMetadata.Path)); }
            foreach (var row in await context.ProjectDeletionOperations.AsNoTracking().ToArrayAsync(token)) foreach (var path in ProjectLifecycleStore.Decode(row).OriginalPaths) originals.Add(Path.GetFullPath(path));
        }
        public async Task BackupAsync(string stagingRoot, Guid operationId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var auditRoot = Path.Combine(stagingRoot, "Backups", "storage-migration-" + operationId.ToString("N"));
            Directory.CreateDirectory(auditRoot);
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(sourceRoot, "finalcheck.db"),
                Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5 }.ToString());
            await source.OpenAsync(cancellationToken);
            foreach (var target in new[] { Path.Combine(stagingRoot, "finalcheck.db"), Path.Combine(auditRoot, "source-finalcheck.db") })
            {
                await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
                await destination.OpenAsync(cancellationToken);
                source.BackupDatabase(destination);
            }
            await new SqliteDataRootDatabaseInspector().ValidateAsync(Path.Combine(stagingRoot, "finalcheck.db"), cancellationToken);
        }
        public async Task RelocateAndValidateAsync(string physicalRoot, string logicalRoot, CancellationToken cancellationToken = default)
        {
            await using var context = Open(physicalRoot);
            var before = await FactsAsync(context, sourceRoot, cancellationToken);
            CompareFacts(before);
            foreach (var row in await context.RestoredWorkingCopies.ToArrayAsync(cancellationToken))
            {
                var copy = DecodeCopy(row.Payload);
                row.Payload = JsonSerializer.SerializeToUtf8Bytes(RelocateCopy(copy, sourceRoot, logicalRoot));
            }
            foreach (var row in await context.FormatRestoreOperations.ToArrayAsync(cancellationToken))
            {
                var operation = DecodeOperation(row.Payload);
                row.Payload = JsonSerializer.SerializeToUtf8Bytes(RelocateOperation(operation, sourceRoot, logicalRoot));
            }
            await context.SaveChangesAsync(cancellationToken); // Target only; source never receives DML.
            context.ChangeTracker.Clear();
            CompareFacts(await FactsAsync(context, logicalRoot, cancellationToken));
            await ValidateManagedAsync(context, physicalRoot, logicalRoot, cancellationToken);
        }
        public async Task ValidateReopenedAsync(string targetRoot, CancellationToken cancellationToken = default)
        {
            await new SqliteDataRootDatabaseInspector().ValidateAsync(Path.Combine(targetRoot, "finalcheck.db"), cancellationToken);
            await using var context = Open(targetRoot);
            CompareFacts(await FactsAsync(context, targetRoot, cancellationToken));
            await ValidateManagedAsync(context, targetRoot, targetRoot, cancellationToken);
            await using var old = Open(sourceRoot);
            CompareFacts(await FactsAsync(old, sourceRoot, cancellationToken));
        }
        private void CompareFacts(Dictionary<string, string> actual)
        {
            if (actual.Count != _expected.Count || _expected.Any(pair => !actual.TryGetValue(pair.Key, out var value) || value != pair.Value))
                throw new InvalidDataException("Database identities/counts/history digest mismatch.");
        }
        private async Task<Dictionary<string, string>> FactsAsync(FinalCheckDbContext context, string logicalRoot, CancellationToken token)
        {
            var facts = new Dictionary<string, string>();
            foreach (var row in await context.ProjectDeletionOperations.AsNoTracking().ToArrayAsync(token))
            {
                var journal = ProjectLifecycleStore.Decode(row);
                if (journal.Files.Any(f => Path.IsPathFullyQualified(f.RelativePath) || f.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p is ".." or ".") || f.Sha256.Length != 64)) throw new InvalidDataException("Invalid deletion cleanup paths.");
                facts.Add("project-deletion:" + row.Id, Hash(JsonSerializer.SerializeToUtf8Bytes(row)));
            }
            foreach (var row in await context.ProjectComparisons.AsNoTracking().ToArrayAsync(token))
            {
                if (!Enum.IsDefined((Core.Management.ProjectBaselineType)row.BaselineType) ||
                    (row.BaselineType == (int)Core.Management.ProjectBaselineType.Own ? row.OwnBaselineVersionId is null || row.TemplateBaselineVersionId is not null : row.TemplateBaselineVersionId is null || row.OwnBaselineVersionId is not null) ||
                    !await context.ContractVersions.AnyAsync(v => v.Id == row.CurrentVersionId && v.ProjectId == row.ProjectId && v.SnapshotId == row.CurrentSourceSnapshotId, token)) throw new InvalidDataException("Invalid project comparison context.");
                var baselineValid = row.OwnBaselineVersionId is { } own ? await context.ContractVersions.AnyAsync(v => v.Id == own && v.ProjectId == row.ProjectId && v.Role == (int)Core.Management.ContractVersionRole.Own && v.SnapshotId == row.BaselineSourceSnapshotId, token) : await context.TemplateVersions.AnyAsync(v => v.Id == row.TemplateBaselineVersionId && v.SnapshotId == row.BaselineSourceSnapshotId, token);
                if (!baselineValid) throw new InvalidDataException("Invalid historical baseline identity.");
                facts.Add("project-comparison:" + row.RecordId, Hash(JsonSerializer.SerializeToUtf8Bytes(row)));
            }
            foreach (var row in await context.NegotiationRounds.AsNoTracking().ToArrayAsync(token))
            { if (row.Number < 1) throw new InvalidDataException("Invalid round."); facts.Add("round:" + row.Id, Hash(JsonSerializer.SerializeToUtf8Bytes(row))); }
            foreach (var row in await context.ContractVersions.AsNoTracking().ToArrayAsync(token))
            {
                _ = ContractVersionStore.Map(row); var snapshot = await new DocumentSnapshotStore(context, snapshots).LoadAsync(row.SnapshotId, token);
                if (snapshot is null || snapshot.Metadata.Sha256 != row.Sha256 || row.DuplicateReference is { } duplicate &&
                    !await context.ContractVersions.AnyAsync(v => v.Id == duplicate && v.ProjectId == row.ProjectId && v.Sha256 == row.Sha256, token)) throw new InvalidDataException("Invalid version Snapshot/duplicate identity.");
                facts.Add("version:" + row.Id, Hash(JsonSerializer.SerializeToUtf8Bytes(row)));
            }
            foreach (var row in await context.ProjectFolders.AsNoTracking().ToArrayAsync(token)) facts.Add("folder:" + row.Id, Hash(JsonSerializer.SerializeToUtf8Bytes(row)));
            foreach (var row in await context.Projects.AsNoTracking().ToArrayAsync(token))
            {
                _ = ProjectStore.Map(row);
                if (row.CurrentBaselineVersionId is { } baseline && !await context.ContractVersions.AnyAsync(v => v.Id == baseline && v.ProjectId == row.Id && v.Role == (int)Core.Management.ContractVersionRole.Own, token)) throw new InvalidDataException("Invalid current own baseline.");
                if ((row.BoundTemplateId is null) != (row.BoundTemplateVersionId is null) || row.BoundTemplateId is { } id &&
                    !await context.TemplateVersions.AnyAsync(v => v.Id == row.BoundTemplateVersionId && v.TemplateId == id, token)) throw new InvalidDataException("Invalid project template binding.");
                facts.Add("project:" + row.Id, Hash(JsonSerializer.SerializeToUtf8Bytes(row)));
            }
            foreach (var row in await context.Templates.AsNoTracking().ToArrayAsync(token)) facts.Add("template:" + row.Id, Hash(JsonSerializer.SerializeToUtf8Bytes(row)));
            foreach (var row in await context.TemplateVersions.AsNoTracking().ToArrayAsync(token))
            {
                var snapshot = await new DocumentSnapshotStore(context, snapshots).LoadAsync(row.SnapshotId, token);
                if (snapshot is null || snapshot.Metadata.Sha256 != row.Sha256 || !Path.IsPathFullyQualified(row.FilePath) || !Enum.IsDefined((Core.Documents.DocumentParseStatus)row.ParseStatus))
                    throw new InvalidDataException("Invalid template source/Snapshot identity.");
                facts.Add("template-version:" + row.Id, Hash(JsonSerializer.SerializeToUtf8Bytes(row)));
            }
            foreach (var row in await context.DocumentSnapshots.AsNoTracking().ToArrayAsync(token))
            {
                var snapshot = snapshots.Deserialize(row.Payload);
                using var payload = JsonDocument.Parse(row.Payload);
                if (row.SnapshotSchemaVersion < 1 || row.SnapshotSchemaVersion > Core.Documents.DocumentSnapshot.CurrentSchemaVersion ||
                    payload.RootElement.GetProperty("snapshotSchemaVersion").GetInt32() != row.SnapshotSchemaVersion ||
                    snapshot.Metadata is null || snapshot.Paragraphs is null || snapshot.Tables is null || snapshot.Revisions is null || snapshot.Comments is null ||
                    snapshot.Sections is null || snapshot.Styles is null || snapshot.Numbering is null || snapshot.Defaults is null ||
                    snapshot.Hyperlinks is null || snapshot.Protection is null || snapshot.HeaderFooters is null || snapshot.ParseDiagnostics is null)
                    throw new InvalidDataException("Unknown Snapshot schema.");
                facts.Add("snapshot:" + row.Id, Hash(JsonSerializer.SerializeToUtf8Bytes(new { row.Id, row.SnapshotSchemaVersion, row.Payload, row.CreatedAtUtc })));
            }
            foreach (var row in await context.ComparisonResults.AsNoTracking().ToArrayAsync(token))
            {
                var result = comparisons.Deserialize(row.Payload);
                if (row.ComparisonSchemaVersion != result.ComparisonSchemaVersion || row.AlgorithmVersion != result.Metadata.AlgorithmVersion ||
                    row.BaselineSnapshotId != result.Metadata.BaselineSnapshotId || row.CurrentSnapshotId != result.Metadata.CurrentSnapshotId)
                    throw new InvalidDataException("Inconsistent Comparison metadata.");
                facts.Add("comparison:" + row.Id, Hash(JsonSerializer.SerializeToUtf8Bytes(row)));
            }
            foreach (var row in await context.ComparisonRecords.AsNoTracking().ToArrayAsync(token))
            {
                _ = await new ComparisonRecordStore(context, snapshots, comparisons).LoadAsync(row.Id, token)
                    ?? throw new InvalidDataException("Missing comparison record.");
                facts.Add("record:" + row.Id, Hash(JsonSerializer.SerializeToUtf8Bytes(row)));
            }
            foreach (var row in await context.RestoredWorkingCopies.AsNoTracking().ToArrayAsync(token))
            {
                var copy = DecodeCopy(row.Payload);
                if (copy.ContractVersionId != row.ContractVersionId || copy.Sha256 != row.Sha256)
                    throw new InvalidDataException("Inconsistent Working Copy metadata.");
                var normalized = RelocateCopy(copy, logicalRoot, sourceRoot);
                facts.Add("copy:" + row.ContractVersionId, Hash(JsonSerializer.SerializeToUtf8Bytes(new { row.ContractVersionId, row.Sha256, row.UpdatedAtUtc, Copy = normalized })));
            }
            foreach (var row in await context.FormatRestoreOperations.AsNoTracking().ToArrayAsync(token))
            {
                var operation = DecodeOperation(row.Payload);
                if (operation.OperationId != row.Id || operation.ContractVersionId != row.ContractVersionId || operation.Status.ToString() != row.Status ||
                    operation.SchemaVersion != row.SchemaVersion) throw new InvalidDataException("Inconsistent restore operation metadata.");
                var normalized = RelocateOperation(operation, logicalRoot, sourceRoot);
                facts.Add("restore:" + row.Id, Hash(JsonSerializer.SerializeToUtf8Bytes(new { row.Id, row.ContractVersionId, row.SchemaVersion, row.Status, row.CreatedAtUtc, Operation = normalized })));
            }
            return facts;
        }
        private async Task ValidateManagedAsync(FinalCheckDbContext context, string physicalRoot, string logicalRoot, CancellationToken token)
        {
            foreach (var row in await context.RestoredWorkingCopies.AsNoTracking().ToArrayAsync(token))
            {
                var copy = DecodeCopy(row.Payload);
                var physical = Path.Combine(physicalRoot, Path.GetRelativePath(logicalRoot, copy.WorkingPath));
                var parsed = await parser.ParseFileAsync(physical, cancellationToken: token);
                if (parsed.Metadata.Sha256 != copy.Sha256 || !snapshots.Serialize(parsed).SequenceEqual(snapshots.Serialize(copy.Snapshot)))
                    throw new InvalidDataException("Working Copy reparse/Snapshot validation failed.");
                if (!await context.FormatRestoreOperations.AnyAsync(o => o.Id == copy.LastOperationId && o.ContractVersionId == copy.ContractVersionId, token))
                    throw new InvalidDataException("Working Copy operation chain is missing.");
            }
            foreach (var row in await context.FormatRestoreOperations.AsNoTracking().ToArrayAsync(token))
            {
                var operation = DecodeOperation(row.Payload);
                if (operation.UndoesOperationId is { } parent && !await context.FormatRestoreOperations.AnyAsync(o => o.Id == parent && o.ContractVersionId == row.ContractVersionId, token))
                    throw new InvalidDataException("Undo chain is missing.");
                if (operation.BackupPath.Length > 0)
                {
                    var backup = Path.Combine(physicalRoot, Path.GetRelativePath(logicalRoot, operation.BackupPath));
                    if (File.Exists(backup) && operation.BeforeSha256 is not null)
                    {
                        await using var stream = File.OpenRead(backup);
                        if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(operation.BeforeSha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Restore backup hash mismatch.");
                    }
                    else if (operation.BeforeSha256 is not null && operation.Status is FormatRestoreOperationStatus.Completed or FormatRestoreOperationStatus.Undone)
                        throw new InvalidDataException("Completed restore backup is missing.");
                }
            }
        }
        private static RestoredWorkingCopy DecodeCopy(byte[] bytes)
        {
            var copy = JsonSerializer.Deserialize<RestoredWorkingCopy>(bytes, StrictJson) ?? throw new InvalidDataException("Invalid Working Copy JSON.");
            if (copy.ContractVersionId == Guid.Empty || !Path.IsPathFullyQualified(copy.OriginalPath) ||
                copy.Snapshot.SnapshotSchemaVersion != Core.Documents.DocumentSnapshot.CurrentSchemaVersion || copy.Snapshot.Metadata.Sha256 != copy.Sha256)
                throw new InvalidDataException("Unknown Working Copy schema/identity.");
            return copy;
        }
        private static FormatRestoreOperation DecodeOperation(byte[] bytes)
        {
            var operation = JsonSerializer.Deserialize<FormatRestoreOperation>(bytes, StrictJson) ?? throw new InvalidDataException("Invalid restore JSON.");
            if (operation.SchemaVersion != FormatRestoreOperation.CurrentSchemaVersion || operation.Plan is { SchemaVersion: not FormatRestorePlan.CurrentSchemaVersion } ||
                operation.WorkingCopy.ContractVersionId != operation.ContractVersionId || operation.WorkingCopy.Sha256 != operation.AfterSha256 ||
                operation.WorkingCopy.Snapshot.Metadata.Sha256 != operation.AfterSha256)
                throw new InvalidDataException("Unknown restore schema/identity.");
            return operation;
        }
        private static RestoredWorkingCopy RelocateCopy(RestoredWorkingCopy copy, string from, string to)
        {
            var expected = Path.Combine(from, "WorkingCopies", copy.ContractVersionId.ToString("N"), "restored.docx");
            if (copy.WorkingPath != expected) throw new InvalidDataException("Working path is outside the exact managed layout.");
            return copy with { WorkingPath = Path.Combine(to, "WorkingCopies", copy.ContractVersionId.ToString("N"), "restored.docx") };
        }
        private static FormatRestoreOperation RelocateOperation(FormatRestoreOperation operation, string from, string to)
        {
            string Map(string path, string name)
            {
                if (path.Length == 0) return path;
                if (path != Path.Combine(from, "WorkingCopies", operation.ContractVersionId.ToString("N"), name))
                    throw new InvalidDataException("Journal path is outside the exact managed layout.");
                return Path.Combine(to, "WorkingCopies", operation.ContractVersionId.ToString("N"), name);
            }
            return operation with { WorkingCopy = RelocateCopy(operation.WorkingCopy, from, to),
                TemporaryPath = Map(operation.TemporaryPath, $"candidate-{operation.OperationId:N}.docx"),
                BackupPath = Map(operation.BackupPath, $"previous-{operation.OperationId:N}.docx") };
        }
        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); } finally { await guard.DisposeAsync(); }
        }
    }
}
