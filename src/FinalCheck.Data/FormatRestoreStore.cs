using System.Text.Json;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Formatting;
using FinalCheck.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data;

public sealed class FormatRestoreStore(FinalCheckDbContext context, IDocumentSnapshotSerializer snapshots,
    IComparisonResultSerializer comparisons) : IFormatRestoreStore
{
    public async Task<RestoredWorkingCopy?> LoadWorkingCopyAsync(Guid contractVersionId, CancellationToken cancellationToken = default)
    {
        var entity = await context.RestoredWorkingCopies.AsNoTracking().SingleOrDefaultAsync(c => c.ContractVersionId == contractVersionId, cancellationToken);
        if (entity is null) return null;
        var copy = JsonSerializer.Deserialize<RestoredWorkingCopy>(entity.Payload) ?? throw new InvalidDataException("Invalid working copy payload.");
        if (copy.ContractVersionId != entity.ContractVersionId || copy.Sha256 != entity.Sha256 ||
            copy.Snapshot.SnapshotSchemaVersion != Core.Documents.DocumentSnapshot.CurrentSchemaVersion || copy.Snapshot.Metadata.Sha256 != copy.Sha256)
            throw new InvalidDataException("Inconsistent working copy metadata/schema.");
        return copy;
    }

    public async Task<FormatRestoreOperation?> LoadLastOperationAsync(Guid contractVersionId, CancellationToken cancellationToken = default)
    {
        var copy = await LoadWorkingCopyAsync(contractVersionId, cancellationToken);
        if (copy is null) return null;
        var operation = await context.FormatRestoreOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == copy.LastOperationId, cancellationToken);
        return operation is null ? null : Decode(operation);
    }

    public async Task<IReadOnlyList<FormatRestoreOperation>> LoadPendingAsync(Guid contractVersionId, CancellationToken cancellationToken = default) =>
        (await context.FormatRestoreOperations.AsNoTracking().Where(o => o.ContractVersionId == contractVersionId && o.Status == "Prepared")
            .OrderBy(o => o.CreatedAtUtc).ToArrayAsync(cancellationToken)).Select(Decode).ToArray();

    public async Task SavePreparedAsync(FormatRestoreOperation operation, CancellationToken cancellationToken = default)
    {
        if (operation.Status != FormatRestoreOperationStatus.Prepared || operation.SchemaVersion != FormatRestoreOperation.CurrentSchemaVersion)
            throw new InvalidDataException("Invalid prepared operation.");
        ValidateWorkingCopy(operation);
        context.FormatRestoreOperations.Add(Encode(operation));
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }

    public async Task CompleteAsync(FormatRestoreOperation operation, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var row = await context.FormatRestoreOperations.SingleAsync(o => o.Id == operation.OperationId, cancellationToken);
            if (row.Status != "Prepared") throw new InvalidDataException("Operation is no longer prepared.");
            var completed = operation with { Status = FormatRestoreOperationStatus.Completed, CompletedAt = DateTimeOffset.UtcNow };
            row.Status = "Completed";
            row.Payload = JsonSerializer.SerializeToUtf8Bytes(completed);
            var copy = await context.RestoredWorkingCopies.SingleOrDefaultAsync(c => c.ContractVersionId == operation.ContractVersionId, cancellationToken);
            if (copy?.Sha256 != operation.PreviousMetadataSha256) throw new DbUpdateConcurrencyException("Working copy metadata changed.");
            if (copy is null) { copy = new StoredRestoredWorkingCopy { ContractVersionId = operation.ContractVersionId }; context.RestoredWorkingCopies.Add(copy); }
            copy.Sha256 = operation.AfterSha256;
            copy.Payload = JsonSerializer.SerializeToUtf8Bytes(operation.WorkingCopy);
            copy.UpdatedAtUtc = operation.WorkingCopy.UpdatedAt.UtcDateTime;
            if (operation.UndoesOperationId is { } parentId)
            {
                var parent = await context.FormatRestoreOperations.SingleAsync(o => o.Id == parentId, cancellationToken);
                var previous = Decode(parent) with { Status = FormatRestoreOperationStatus.Undone };
                parent.Status = "Undone"; parent.Payload = JsonSerializer.SerializeToUtf8Bytes(previous);
            }
            if (operation.Kind == FormatRestoreOperationKind.PreserveExternalChanges)
            {
                var snapshot = operation.WorkingCopy.Snapshot;
                context.DocumentSnapshots.Add(new StoredDocumentSnapshot { Id = Guid.NewGuid(), SnapshotSchemaVersion = snapshot.SnapshotSchemaVersion,
                    Payload = snapshots.Serialize(snapshot), CreatedAtUtc = DateTime.UtcNow });
                var comparison = operation.RebuiltComparison ?? throw new InvalidDataException("Missing rebuilt comparison.");
                context.ComparisonResults.Add(new StoredComparisonResult { Id = Guid.NewGuid(), ComparisonSchemaVersion = comparison.ComparisonSchemaVersion,
                    BaselineSnapshotId = comparison.Metadata.BaselineSnapshotId, CurrentSnapshotId = comparison.Metadata.CurrentSnapshotId,
                    AlgorithmVersion = comparison.Metadata.AlgorithmVersion, Payload = comparisons.Serialize(comparison), CreatedAtUtc = DateTime.UtcNow });
            }
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { context.ChangeTracker.Clear(); }
    }

    public async Task FailAsync(FormatRestoreOperation operation, CancellationToken cancellationToken = default)
    {
        context.ChangeTracker.Clear();
        var row = await context.FormatRestoreOperations.SingleAsync(o => o.Id == operation.OperationId, cancellationToken);
        var failed = operation with { Status = FormatRestoreOperationStatus.Failed, CompletedAt = DateTimeOffset.UtcNow };
        row.Status = "Failed"; row.Payload = JsonSerializer.SerializeToUtf8Bytes(failed);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }

    private static StoredFormatRestoreOperation Encode(FormatRestoreOperation operation) => new()
    {
        Id = operation.OperationId, ContractVersionId = operation.ContractVersionId, SchemaVersion = operation.SchemaVersion,
        Status = operation.Status.ToString(), Payload = JsonSerializer.SerializeToUtf8Bytes(operation), CreatedAtUtc = operation.CreatedAt.UtcDateTime,
    };

    private static FormatRestoreOperation Decode(StoredFormatRestoreOperation entity)
    {
        var operation = JsonSerializer.Deserialize<FormatRestoreOperation>(entity.Payload) ?? throw new InvalidDataException("Invalid restore payload.");
        if (entity.SchemaVersion != FormatRestoreOperation.CurrentSchemaVersion || operation.SchemaVersion != entity.SchemaVersion ||
            operation.OperationId != entity.Id || operation.ContractVersionId != entity.ContractVersionId || operation.Status.ToString() != entity.Status ||
            operation.Plan is { SchemaVersion: not FormatRestorePlan.CurrentSchemaVersion })
            throw new InvalidDataException("Unknown or inconsistent restore operation schema/metadata.");
        ValidateWorkingCopy(operation);
        return operation;
    }

    private static void ValidateWorkingCopy(FormatRestoreOperation operation)
    {
        var copy = operation.WorkingCopy;
        if (copy.ContractVersionId != operation.ContractVersionId || copy.Sha256 != operation.AfterSha256 ||
            copy.Snapshot.SnapshotSchemaVersion != Core.Documents.DocumentSnapshot.CurrentSchemaVersion || copy.Snapshot.Metadata.Sha256 != copy.Sha256)
            throw new InvalidDataException("Operation/working Snapshot identity mismatch.");
    }
}
