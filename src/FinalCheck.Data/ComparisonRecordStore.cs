using System.Text.Json;
using System.Text.Json.Serialization;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Management;
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
        if (record.SchemaVersion is not (1 or ComparisonRecord.CurrentSchemaVersion) || record.RecordId == Guid.Empty ||
            record.BaselineSnapshotId == Guid.Empty || record.CurrentSnapshotId == Guid.Empty || record.ResultId == Guid.Empty ||
            record.BaselineFile is null || record.CurrentFile is null ||
            !Path.IsPathFullyQualified(record.BaselineFile.Path ?? "") || !Path.IsPathFullyQualified(record.CurrentFile.Path ?? "") ||
            record.BaselineFile.Sha256?.Length != 64 || record.CurrentFile.Sha256?.Length != 64 ||
            record.ReviewStates is null || record.ReviewStates.Values.Any(value => !Enum.IsDefined(value)) ||
            record.IgnoreRules is null || record.IgnoreRules.Characters is null || record.IgnoreRules.HiddenProperties.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Unknown comparison record schema/identity.");
        return record;
    }
    public async Task<ComparisonWorkflowResult> SaveAsync(ComparisonFile baselineFile, ComparisonFile currentFile,
        DocumentSnapshot baseline, DocumentSnapshot current, ComparisonResult result, ComparisonIgnoreRules? ignoreRules = null,
        CancellationToken cancellationToken = default)
    {
        var baselineId = Guid.NewGuid(); var currentId = Guid.NewGuid(); var resultId = Guid.NewGuid();
        var record = new ComparisonRecord(ComparisonRecord.CurrentSchemaVersion, Guid.NewGuid(), baselineFile, currentFile, baselineId, currentId,
            resultId, DateTimeOffset.UtcNow, result.Changes.ToDictionary(c => c.ChangeId, _ => ComparisonReviewState.Unresolved, StringComparer.Ordinal))
        { IgnoreRules = ignoreRules ?? new() };
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
    public async Task<ComparisonWorkflowResult> SaveAutomaticProjectAsync(ComparisonFile baselineFile, ComparisonFile currentFile,
        DocumentSnapshot baseline, DocumentSnapshot current, ComparisonResult result,
        AutomaticTemplateBaseline? templateBaseline = null, ComparisonIgnoreRules? ignoreRules = null,
        CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is not null) throw new InvalidOperationException("Automatic project save owns its transaction.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (templateBaseline is not null)
        {
            var valid = await context.TemplateVersions.AsNoTracking().AnyAsync(x => x.Id == templateBaseline.TemplateVersionId &&
                x.TemplateId == templateBaseline.TemplateId && x.SnapshotId == templateBaseline.SnapshotId && x.Sha256 == baselineFile.Sha256, cancellationToken);
            if (!valid || !await context.Templates.AsNoTracking().AnyAsync(x => x.Id == templateBaseline.TemplateId && x.IsEnabled && !x.IsDeleted, cancellationToken))
                throw new InvalidDataException("所选模板版本已变化或不可用，请重新匹配。 ");
        }
        var saved = await SaveAsync(baselineFile, currentFile, baseline, current, result, ignoreRules, cancellationToken);
        var projectId = Guid.NewGuid(); var now = saved.Record.CreatedAt.UtcDateTime;
        var suggested = (templateBaseline?.TemplateName ?? Path.GetFileNameWithoutExtension(baselineFile.Name)).Trim();
        if (string.IsNullOrWhiteSpace(suggested)) suggested = "未命名合同";
        if (suggested.Length > 100) suggested = suggested[..100];
        var sameContent = baselineFile.Sha256.Equals(currentFile.Sha256, StringComparison.OrdinalIgnoreCase);
        context.Projects.Add(new StoredProject { Id = projectId, ProjectName = suggested, Status = (int)ProjectStatus.Active,
            CreatedAtUtc = now, UpdatedAtUtc = now, NextVersionNumber = sameContent ? 2 : 3,
            BoundTemplateId = templateBaseline?.TemplateId, BoundTemplateVersionId = templateBaseline?.TemplateVersionId });
        context.NegotiationRounds.Add(new StoredNegotiationRound { Id = Guid.NewGuid(), ProjectId = projectId, Number = 1 });
        StoredContractVersion NewVersion(ComparisonFile file, Guid snapshotId, DocumentSnapshot snapshot, int number) => new()
        {
            Id = Guid.NewGuid(), ProjectId = projectId, FilePath = file.Path, FileName = file.Name, FileSize = file.Size,
            ModifiedAtUtc = file.ModifiedAt.UtcDateTime, Sha256 = file.Sha256, SnapshotId = snapshotId,
            Role = (int)ContractVersionRole.Unspecified, RoundNumber = 1, ImportedAtUtc = now.AddTicks(number - 1),
            VersionNumber = number, OriginalSourceJson = JsonSerializer.Serialize(file), ParseStatus = (int)snapshot.ParseStatus,
        };
        var baselineVersion = NewVersion(baselineFile, saved.Record.BaselineSnapshotId, baseline, 1);
        var currentVersion = sameContent ? baselineVersion : NewVersion(currentFile, saved.Record.CurrentSnapshotId, current, 2);
        context.ContractVersions.Add(baselineVersion);
        if (!sameContent) context.ContractVersions.Add(currentVersion);
        context.ProjectComparisons.Add(new StoredProjectComparison { RecordId = saved.Record.RecordId, ProjectId = projectId,
            CurrentVersionId = currentVersion.Id, BaselineType = (int)(templateBaseline is null ? ProjectBaselineType.Version : ProjectBaselineType.Template),
            BaselineContractVersionId = templateBaseline is null ? baselineVersion.Id : null,
            TemplateBaselineVersionId = templateBaseline?.TemplateVersionId, BaselineSourceSnapshotId = templateBaseline?.SnapshotId ?? baselineVersion.SnapshotId,
            CurrentSourceSnapshotId = currentVersion.SnapshotId, BaselineName = baselineFile.Name, CurrentName = currentFile.Name,
            TotalChanges = result.Changes.Count, CreatedAtUtc = now });
        await context.SaveChangesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None);
        return saved;
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
        return await WriteReviewAsync(recordId, changeIds.Distinct(StringComparer.Ordinal).ToDictionary(id => id, _ => state, StringComparer.Ordinal), null, cancellationToken);
    }
    public Task<ComparisonRecord> EditReviewAsync(Guid recordId, ComparisonReviewEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.States.Count == 0 || !edit.States.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(edit.ExpectedStates.Keys) ||
            edit.States.Values.Concat(edit.ExpectedStates.Values).Any(state => !Enum.IsDefined(state)))
            throw new ArgumentException("Invalid atomic review edit.");
        return WriteReviewAsync(recordId, edit.States, edit.ExpectedStates, cancellationToken);
    }
    private async Task<ComparisonRecord> WriteReviewAsync(Guid recordId, IReadOnlyDictionary<string, ComparisonReviewState> changes,
        IReadOnlyDictionary<string, ComparisonReviewState>? expected, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var row = await context.ComparisonRecords.AsNoTracking().SingleAsync(r => r.Id == recordId, cancellationToken);
            var record = Decode(row.Payload); var states = record.ReviewStates.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            foreach (var (id, state) in changes)
            {
                if (!states.TryGetValue(id, out var previous)) throw new ArgumentException("Change is not part of this comparison.");
                if (expected is not null && previous != expected[id])
                    throw new DbUpdateConcurrencyException("This change was reviewed elsewhere; reload before editing or undoing.");
                states[id] = state;
            }
            var updated = record with { ReviewStates = states }; var payload = JsonSerializer.SerializeToUtf8Bytes(updated);
            // Compare-and-swap merges with freshly loaded states instead of overwriting another operation's review work.
            var count = await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ComparisonRecords SET Payload = {payload} WHERE Id = {recordId} AND Payload = {row.Payload}", cancellationToken);
            if (count == 1) return updated;
        }
        throw new DbUpdateConcurrencyException("Review states changed concurrently; reload and retry.");
    }
}
