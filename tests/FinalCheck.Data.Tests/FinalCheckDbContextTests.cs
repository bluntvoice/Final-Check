using FinalCheck.Comparison;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Data;
using FinalCheck.Data.Entities;
using FinalCheck.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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

    [Fact]
    public async Task ComparisonResultStorePersistsPayloadAndMetadataRoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FinalCheck.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "comparison.db");

        try
        {
            var options = new DbContextOptionsBuilder<FinalCheckDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            await using var context = new FinalCheckDbContext(options);
            await context.Database.MigrateAsync();
            var serializer = new JsonComparisonResultSerializer();
            var store = new ComparisonResultStore(context, serializer);
            var result = new ComparisonResult(
                ComparisonResult.CurrentSchemaVersion,
                new ComparisonMetadata("baseline", "current", 2, 2, "comparison-v0.1"),
                [],
                [new ComparisonChangeItem(
                    "text:p0:p0",
                    ComparisonChangeKind.TextReplace,
                    "p0",
                    "p0",
                    "body/p[0]",
                    "body/p[0]",
                    "30日",
                    "60日",
                    [new DifferenceSpan(DifferenceOperation.Replace, 0, 2, 0, 2, "30", "60")],
                    null,
                    [],
                    [],
                    [ComparisonEvidenceKind.SnapshotDifference],
                    ComparisonConfidenceLevel.High,
                    [])],
                [],
                [],
                ComparisonStatistics.Empty with { TotalChanges = 1, TextChanges = 1 });

            var id = await store.SaveAsync(result);
            context.ChangeTracker.Clear();
            var loaded = await store.LoadAsync(id);
            var entity = await context.ComparisonResults.SingleAsync(comparison => comparison.Id == id);

            Assert.NotNull(loaded);
            Assert.Equal(serializer.Serialize(result), serializer.Serialize(loaded));
            Assert.Equal("baseline", entity.BaselineSnapshotId);
            Assert.Equal("current", entity.CurrentSnapshotId);
            Assert.Equal(DateTimeKind.Utc, entity.CreatedAtUtc.Kind);
            Assert.Equal(4, (await context.Database.GetAppliedMigrationsAsync()).Count());
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

    [Fact]
    public async Task ComparisonMigrationPreservesExistingDocumentSnapshots()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FinalCheck.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "upgrade.db");

        try
        {
            var options = new DbContextOptionsBuilder<FinalCheckDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            await using var context = new FinalCheckDbContext(options);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20260910021629_InitialCreate");
            var documentId = Guid.NewGuid();
            context.DocumentSnapshots.Add(new StoredDocumentSnapshot
            {
                Id = documentId,
                SnapshotSchemaVersion = DocumentSnapshot.CurrentSchemaVersion,
                Payload = [1, 2, 3],
                CreatedAtUtc = DateTime.UtcNow,
            });
            await context.SaveChangesAsync();

            await migrator.MigrateAsync();
            context.ChangeTracker.Clear();

            Assert.True(await context.DocumentSnapshots.AnyAsync(snapshot => snapshot.Id == documentId));
            Assert.Equal(4, (await context.Database.GetAppliedMigrationsAsync()).Count());
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
