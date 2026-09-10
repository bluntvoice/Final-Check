using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Documents;
using FinalCheck.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data;

public sealed class DocumentSnapshotStore(
    FinalCheckDbContext dbContext,
    IDocumentSnapshotSerializer serializer)
{
    public async Task<Guid> SaveAsync(
        DocumentSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var entity = new StoredDocumentSnapshot
        {
            Id = Guid.NewGuid(),
            SnapshotSchemaVersion = snapshot.SnapshotSchemaVersion,
            Payload = serializer.Serialize(snapshot),
            CreatedAtUtc = DateTime.UtcNow,
        };
        dbContext.DocumentSnapshots.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<DocumentSnapshot?> LoadAsync(
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.DocumentSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(snapshot => snapshot.Id == snapshotId, cancellationToken);
        return entity is null ? null : serializer.Deserialize(entity.Payload);
    }
}
