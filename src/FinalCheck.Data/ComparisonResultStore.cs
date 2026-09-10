using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data;

public sealed class ComparisonResultStore(
    FinalCheckDbContext dbContext,
    IComparisonResultSerializer serializer) : IComparisonResultStore
{
    public async Task<Guid> SaveAsync(
        ComparisonResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        var entity = new StoredComparisonResult
        {
            Id = Guid.NewGuid(),
            ComparisonSchemaVersion = result.ComparisonSchemaVersion,
            BaselineSnapshotId = result.Metadata.BaselineSnapshotId,
            CurrentSnapshotId = result.Metadata.CurrentSnapshotId,
            AlgorithmVersion = result.Metadata.AlgorithmVersion,
            Payload = serializer.Serialize(result),
            CreatedAtUtc = DateTime.UtcNow,
        };
        dbContext.ComparisonResults.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<ComparisonResult?> LoadAsync(
        Guid comparisonId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ComparisonResults
            .AsNoTracking()
            .SingleOrDefaultAsync(comparison => comparison.Id == comparisonId, cancellationToken);
        return entity is null ? null : serializer.Deserialize(entity.Payload);
    }
}
