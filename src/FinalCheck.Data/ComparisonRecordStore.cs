using System.Text.Json;
using System.Text.Json.Serialization;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data;

public sealed class ComparisonRecordStore(FinalCheckDbContext context, IDocumentSnapshotSerializer snapshots,
    IComparisonResultSerializer comparisons) : IComparisonRecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static ComparisonRecord Decode(byte[] payload)
    {
        var record = JsonSerializer.Deserialize<ComparisonRecord>(payload, JsonOptions) ?? throw new InvalidDataException("Invalid comparison record.");
        if (record.SchemaVersion != ComparisonRecord.CurrentSchemaVersion || record.RecordId == Guid.Empty ||
            record.BaselineSnapshotId == Guid.Empty || record.CurrentSnapshotId == Guid.Empty || record.ResultId == Guid.Empty ||
            record.BaselineFile is null || record.CurrentFile is null ||
            !Path.IsPathFullyQualified(record.BaselineFile.Path ?? "") || !Path.IsPathFullyQualified(record.CurrentFile.Path ?? "") ||
            record.BaselineFile.Sha256?.Length != 64 || record.CurrentFile.Sha256?.Length != 64 ||
            record.ReviewStates is null || record.ReviewStates.Values.Any(value => !Enum.IsDefined(value)))
            throw new InvalidDataException("Unknown comparison record schema/identity.");
        return record;
    }
    public async Task<ComparisonWorkflowResult> SaveAsync(ComparisonFile baselineFile, ComparisonFile currentFile,
        DocumentSnapshot baseline, DocumentSnapshot current, ComparisonResult result, CancellationToken cancellationToken = default)
    {
        var baselineId = Guid.NewGuid(); var currentId = Guid.NewGuid(); var resultId = Guid.NewGuid();
        var record = new ComparisonRecord(1, Guid.NewGuid(), baselineFile, currentFile, baselineId, currentId,
            resultId, DateTimeOffset.UtcNow, result.Changes.ToDictionary(c => c.ChangeId, _ => ComparisonReviewState.Unresolved, StringComparer.Ordinal));
        if (baseline.Metadata.Sha256 != baselineFile.Sha256 || current.Metadata.Sha256 != currentFile.Sha256)
            throw new InvalidDataException("Input identity changed before persistence.");
        // Project comparisons own a wider transaction including immutable context linkage.
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        context.DocumentSnapshots.AddRange(new StoredDocumentSnapshot { Id = baselineId, SnapshotSchemaVersion = baseline.SnapshotSchemaVersion,
            Payload = snapshots.Serialize(baseline), CreatedAtUtc = record.CreatedAt.UtcDateTime },
            new StoredDocumentSnapshot { Id = currentId, SnapshotSchemaVersion = current.SnapshotSchemaVersion,
            Payload = snapshots.Serialize(current), CreatedAtUtc = record.CreatedAt.UtcDateTime });
        context.ComparisonResults.Add(new StoredComparisonResult { Id = resultId, ComparisonSchemaVersion = result.ComparisonSchemaVersion,
            BaselineSnapshotId = result.Metadata.BaselineSnapshotId, CurrentSnapshotId = result.Metadata.CurrentSnapshotId,
            AlgorithmVersion = result.Metadata.AlgorithmVersion, Payload = comparisons.Serialize(result), CreatedAtUtc = record.CreatedAt.UtcDateTime });
        context.ComparisonRecords.Add(new StoredComparisonRecord { Id = record.RecordId, BaselineSnapshotId = baselineId,
            CurrentSnapshotId = currentId, ResultId = resultId, Payload = JsonSerializer.SerializeToUtf8Bytes(record), CreatedAtUtc = record.CreatedAt.UtcDateTime });
        await context.SaveChangesAsync(cancellationToken);
        // Once the commit boundary is reached, cancellation must not report an already saved result as cancelled.
        cancellationToken.ThrowIfCancellationRequested();
        if (transaction is not null) await transaction.CommitAsync(CancellationToken.None);
        return new(record, baseline, current, result);
    }
    public async Task<ComparisonWorkflowResult?> LoadAsync(Guid recordId, CancellationToken cancellationToken = default)
    {
        var row = await context.ComparisonRecords.AsNoTracking().SingleOrDefaultAsync(r => r.Id == recordId, cancellationToken);
        if (row is null) return null;
        var record = Decode(row.Payload);
        if (record.RecordId != row.Id || record.BaselineSnapshotId != row.BaselineSnapshotId || record.CurrentSnapshotId != row.CurrentSnapshotId || record.ResultId != row.ResultId)
            throw new InvalidDataException("Inconsistent comparison record references.");
        var baseline = await new DocumentSnapshotStore(context, snapshots).LoadAsync(row.BaselineSnapshotId, cancellationToken) ?? throw new InvalidDataException("Missing baseline.");
        var current = await new DocumentSnapshotStore(context, snapshots).LoadAsync(row.CurrentSnapshotId, cancellationToken) ?? throw new InvalidDataException("Missing current version.");
        var result = await new ComparisonResultStore(context, comparisons).LoadAsync(row.ResultId, cancellationToken) ?? throw new InvalidDataException("Missing result.");
        if (baseline.Metadata.Sha256 != record.BaselineFile.Sha256 || current.Metadata.Sha256 != record.CurrentFile.Sha256 ||
            result.Metadata.BaselineSnapshotId != "sha256:" + baseline.Metadata.Sha256 || result.Metadata.CurrentSnapshotId != "sha256:" + current.Metadata.Sha256 ||
            !result.Changes.Select(c => c.ChangeId).ToHashSet(StringComparer.Ordinal).SetEquals(record.ReviewStates.Keys))
            throw new InvalidDataException("Inconsistent frozen comparison history.");
        return new(record, baseline, current, result);
    }
    public async Task<IReadOnlyList<ComparisonRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.ComparisonRecords.AsNoTracking().OrderByDescending(r => r.CreatedAtUtc).Take(50).ToArrayAsync(cancellationToken);
        return rows.Select(row => Decode(row.Payload)).ToArray();
    }
    public async Task<ComparisonRecord> UpdateReviewAsync(Guid recordId, IReadOnlyList<string> changeIds,
        ComparisonReviewState state, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(state) || changeIds.Count == 0) throw new ArgumentException("Invalid review operation.");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var row = await context.ComparisonRecords.AsNoTracking().SingleAsync(r => r.Id == recordId, cancellationToken);
            var record = Decode(row.Payload); var states = record.ReviewStates.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            foreach (var id in changeIds)
            { if (!states.ContainsKey(id)) throw new ArgumentException("Change is not part of this comparison."); states[id] = state; }
            var updated = record with { ReviewStates = states }; var payload = JsonSerializer.SerializeToUtf8Bytes(updated);
            // Compare-and-swap merges with freshly loaded states instead of overwriting another operation's review work.
            var count = await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ComparisonRecords SET Payload = {payload} WHERE Id = {recordId} AND Payload = {row.Payload}", cancellationToken);
            if (count == 1) return updated;
        }
        throw new DbUpdateConcurrencyException("Review states changed concurrently; reload and retry.");
    }
}
