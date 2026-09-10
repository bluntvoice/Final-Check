using FinalCheck.Core.Documents;
using FinalCheck.Data;
using FinalCheck.Data.Entities;
using FinalCheck.Documents;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data.Tests;

public sealed class FinalCheckDbContextTests
{
    [Fact]
    public async Task MigrationCreatesDatabaseAndSnapshotCanRoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FinalCheck.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "test.db");

        try
        {
            var options = new DbContextOptionsBuilder<FinalCheckDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            await using (var context = new FinalCheckDbContext(options))
            {
                await context.Database.MigrateAsync();
                var entity = new StoredDocumentSnapshot
                {
                    Id = Guid.NewGuid(),
                    SnapshotSchemaVersion = 1,
                    Payload = [1, 2, 3],
                    CreatedAtUtc = DateTime.UtcNow,
                };

                context.DocumentSnapshots.Add(entity);
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
                var loaded = await context.DocumentSnapshots.SingleAsync(snapshot => snapshot.Id == entity.Id);

                Assert.Equal(entity.Payload, loaded.Payload);
                Assert.Equal(DateTimeKind.Utc, loaded.CreatedAtUtc.Kind);
                Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
                await context.Database.EnsureDeletedAsync();
            }

            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task DocumentSnapshotStorePersistsSerializedSnapshotRoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FinalCheck.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "snapshot.db");

        try
        {
            var options = new DbContextOptionsBuilder<FinalCheckDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            await using var context = new FinalCheckDbContext(options);
            await context.Database.MigrateAsync();
            var serializer = new JsonDocumentSnapshotSerializer();
            var store = new DocumentSnapshotStore(context, serializer);
            var snapshot = DocumentSnapshot.Empty with
            {
                Paragraphs = [new DocumentParagraphSnapshot(
                    new DocumentNodeIdentitySnapshot(
                        "body/p[0]",
                        null,
                        DocumentNodeKind.Paragraph,
                        "body/p[0]",
                        "/word/document.xml",
                        0),
                    0,
                    "持久化快照",
                    "持久化快照",
                    false,
                    null,
                    [],
                    ParagraphFormatSnapshot.Empty,
                    ParagraphFormatSnapshot.Empty,
                    null)],
            };

            var id = await store.SaveAsync(snapshot);
            context.ChangeTracker.Clear();
            var loaded = await store.LoadAsync(id);

            Assert.NotNull(loaded);
            Assert.Equal(DocumentSnapshot.CurrentSchemaVersion, loaded.SnapshotSchemaVersion);
            Assert.Equal("持久化快照", Assert.Single(loaded.Paragraphs).DisplayText);
            Assert.Equal(serializer.Serialize(snapshot), serializer.Serialize(loaded));
            await context.Database.EnsureDeletedAsync();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
