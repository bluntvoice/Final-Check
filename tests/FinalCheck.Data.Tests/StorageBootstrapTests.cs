using System.Security.Cryptography;
using System.Text.Json;
using FinalCheck.Core.Storage;
using FinalCheck.Data.Entities;
using FinalCheck.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FinalCheck.Data.Tests;

public sealed class StorageBootstrapTests
{
    private static readonly JsonSerializerOptions BootstrapOptions = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task NewUserCreatesBootstrapButDoesNotCreateDatabase()
    {
        using var fixture = new StorageFixture();
        var result = await fixture.Resolver.ResolveAsync();
        Assert.Equal(fixture.Paths.DefaultDataRoot, result.Provider.CurrentDataRoot);
        Assert.Equal("NewUser", result.RecoveryCode);
        Assert.False(result.DatabaseInitialized);
        Assert.False(File.Exists(result.Provider.DatabasePath));
        Assert.Equal(fixture.Bootstrap.Load()!.Current, result.Provider.Descriptor);
        Assert.True(File.Exists(fixture.Bootstrap.BootstrapPath));
    }

    [Fact]
    public async Task NormalBootstrapReusesIdentityWithoutWritingDatabase()
    {
        using var fixture = new StorageFixture();
        var first = await fixture.Resolver.ResolveAsync();
        var second = await fixture.Resolver.ResolveAsync();
        Assert.Equal(first.Provider.Descriptor, second.Provider.Descriptor);
        Assert.Equal("ExistingBootstrap", second.RecoveryCode);
        Assert.False(File.Exists(second.Provider.DatabasePath));
    }

    [Theory]
    [InlineData("20260910021629_InitialCreate")]
    [InlineData(null)]
    public async Task LegacyDatabaseAdoptionPreservesHistoryAndDatabaseBytes(string? migration)
    {
        using var fixture = new StorageFixture();
        var database = Path.Combine(fixture.Paths.LegacyDataRoot, "finalcheck.db");
        await fixture.CreateDatabaseAsync(database, migration);
        var hash = SHA256.HashData(File.ReadAllBytes(database));
        var result = await fixture.Resolver.ResolveAsync();
        Assert.Equal(fixture.Paths.LegacyDataRoot, result.Provider.CurrentDataRoot);
        Assert.True(result.DatabaseInitialized);
        Assert.Equal("AdoptedExistingDatabase", result.RecoveryCode);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(database)));
        await using var context = fixture.OpenDatabase(database);
        Assert.Single(await context.DocumentSnapshots.ToArrayAsync());
        Assert.False(File.Exists(Path.Combine(fixture.Paths.DefaultDataRoot, "finalcheck.db")));
    }

    [Fact]
    public async Task MissingBootstrapAdoptsPreviouslyInitializedDefaultRoot()
    {
        using var fixture = new StorageFixture();
        await fixture.CreateDatabaseAsync(Path.Combine(fixture.Paths.DefaultDataRoot, "finalcheck.db"));
        var result = await fixture.Resolver.ResolveAsync();
        Assert.True(result.DatabaseInitialized);
        Assert.Equal(fixture.Paths.DefaultDataRoot, result.Provider.CurrentDataRoot);
    }

    [Fact]
    public async Task TwoDatabasesWithoutLocatorRequireReview()
    {
        using var fixture = new StorageFixture();
        await fixture.CreateDatabaseAsync(Path.Combine(fixture.Paths.LegacyDataRoot, "finalcheck.db"));
        await fixture.CreateDatabaseAsync(Path.Combine(fixture.Paths.DefaultDataRoot, "finalcheck.db"));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Resolver.ResolveAsync());
        Assert.False(File.Exists(fixture.Bootstrap.BootstrapPath));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":99}")]
    public async Task CorruptBootstrapFailsClosedAndIsNotOverwritten(string json)
    {
        using var fixture = new StorageFixture();
        Directory.CreateDirectory(fixture.Paths.ConfigurationDirectory);
        File.WriteAllText(fixture.Bootstrap.BootstrapPath, json);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Resolver.ResolveAsync());
        Assert.Equal(json, File.ReadAllText(fixture.Bootstrap.BootstrapPath));
        Assert.False(Directory.Exists(fixture.Paths.DefaultDataRoot));
    }

    [Fact]
    public async Task MissingConfiguredRootDoesNotCreateAnEmptyReplacement()
    {
        using var fixture = new StorageFixture();
        var missing = fixture.Descriptor(Path.Combine(fixture.DirectoryPath, "missing"));
        fixture.Bootstrap.Save(new(1, missing, missing, true), null);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => fixture.Resolver.ResolveAsync());
        Assert.False(Directory.Exists(missing.Path));
    }

    [Fact]
    public async Task LastKnownGoodRecoveryRequiresSameCommittedIdentityAndGeneration()
    {
        using var fixture = new StorageFixture();
        await fixture.CreateDatabaseAsync(Path.Combine(fixture.Paths.LegacyDataRoot, "finalcheck.db"));
        var good = (await fixture.Resolver.ResolveAsync()).Provider.Descriptor;
        var locator = fixture.Bootstrap.Load()!;
        // Simulate a damaged location field, not an authorized generation transition.
        File.WriteAllBytes(fixture.Bootstrap.BootstrapPath, JsonSerializer.SerializeToUtf8Bytes(
            locator with { Current = good with { Path = Path.Combine(fixture.DirectoryPath, "unavailable") } }, BootstrapOptions));
        var recovered = await fixture.Resolver.ResolveAsync();
        Assert.Equal("LastKnownGood", recovered.RecoveryCode);
        Assert.Equal(good, recovered.Provider.Descriptor);
        Assert.Equal(good, fixture.Bootstrap.Load()!.Current);
    }

    [Fact]
    public async Task StalePreviousMigrationSourceCannotBeUsedForAutomaticFallback()
    {
        using var fixture = new StorageFixture();
        var good = (await fixture.Resolver.ResolveAsync()).Provider.Descriptor;
        var different = fixture.Descriptor(Path.Combine(fixture.DirectoryPath, "other")) with { Generation = 2 };
        Assert.Throws<InvalidDataException>(() => fixture.Bootstrap.Save(new(1, different, good, true), good.Generation));
        Assert.Equal(good, fixture.Bootstrap.Load()!.Current);
    }

    [Fact]
    public async Task MissingPreviouslyInitializedDatabaseFailsClosed()
    {
        using var fixture = new StorageFixture();
        var root = (await fixture.Resolver.ResolveAsync()).Provider;
        await fixture.CreateDatabaseAsync(root.DatabasePath);
        await fixture.Resolver.MarkDatabaseInitializedAsync();
        File.Move(root.DatabasePath, root.DatabasePath + ".saved");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Resolver.ResolveAsync());
        Assert.False(File.Exists(root.DatabasePath));
        Assert.True(File.Exists(root.DatabasePath + ".saved"));
    }

    [Theory]
    [InlineData("WorkingCopies")]
    [InlineData("Backups")]
    public async Task LegacyManagedHistoryWithoutDatabaseIsNotANewUser(string directory)
    {
        using var fixture = new StorageFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Paths.LegacyDataRoot, directory));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Resolver.ResolveAsync());
        Assert.False(File.Exists(fixture.Bootstrap.BootstrapPath));
    }

    [Fact]
    public async Task WrongRootIdentityIsRejected()
    {
        using var fixture = new StorageFixture();
        var first = await fixture.Resolver.ResolveAsync();
        File.WriteAllBytes(Path.Combine(first.Provider.CurrentDataRoot, DataRootBootstrapResolver.IdentityFileName),
            JsonSerializer.SerializeToUtf8Bytes(first.Provider.Descriptor with { RootId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Resolver.ResolveAsync());
    }

    [Fact]
    public async Task UnknownLegacyDatabaseIsNotAdopted()
    {
        using var fixture = new StorageFixture();
        Directory.CreateDirectory(fixture.Paths.LegacyDataRoot);
        var database = Path.Combine(fixture.Paths.LegacyDataRoot, "finalcheck.db");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE SomethingElse (Id INTEGER)";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<SqliteException>(() => fixture.Resolver.ResolveAsync());
        Assert.False(File.Exists(fixture.Bootstrap.BootstrapPath));
    }

    [Fact]
    public async Task AtomicSaveValidatesGenerationAndPreservesPreviousLocator()
    {
        using var fixture = new StorageFixture();
        await fixture.Resolver.ResolveAsync();
        var original = fixture.Bootstrap.Load()!;
        var next = original.Current with { Generation = 2 };
        var update = original with { Current = next, LastKnownGood = next };
        Assert.Throws<InvalidOperationException>(() => fixture.Bootstrap.Save(update, 0));
        Assert.Equal(original, fixture.Bootstrap.Load());
        fixture.Bootstrap.Save(update, 1);
        Assert.Equal(update, fixture.Bootstrap.Load());
        var previous = JsonSerializer.Deserialize<StorageBootstrap>(File.ReadAllBytes(fixture.Bootstrap.BootstrapPath + ".previous"), BootstrapOptions);
        Assert.Equal(original, previous);
        Assert.Empty(Directory.EnumerateFiles(fixture.Paths.ConfigurationDirectory, "bootstrap.json.*.tmp"));
    }

    [Fact]
    public async Task InstallationOverlapCannotBecomeDataRoot()
    {
        using var fixture = new StorageFixture();
        var bad = fixture.Descriptor(fixture.Paths.InstallDirectory);
        fixture.Bootstrap.Save(new(1, bad, bad, false), null);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Resolver.ResolveAsync());
    }

    [Fact]
    public void ProviderPathsAreImmutableAbsoluteAndDoNotDiscoverMachineUser()
    {
        using var fixture = new StorageFixture();
        var provider = new DataRootPaths(fixture.Descriptor(Path.Combine(fixture.DirectoryPath, "自定义 data;root")));
        Assert.All(new[] { provider.DatabasePath, provider.SnapshotPath, provider.WorkingCopyPath, provider.BackupPath,
            provider.CachePath, provider.LogPath, provider.TempPath }, path => Assert.StartsWith(provider.CurrentDataRoot, path));
        Assert.Equal(provider.DatabasePath, provider.SnapshotPath);
        Assert.Throws<InvalidDataException>(() => new DataRootPaths(fixture.Descriptor("relative")));
        Assert.Throws<InvalidDataException>(() => new DataRootPaths(provider.Descriptor with { LayoutVersion = 99 }));
    }
}

internal sealed class StorageFixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "FinalCheck.Storage.Tests", Guid.NewGuid().ToString("N"));
    public FixturePlatformPaths Paths { get; }
    public FileStorageBootstrapStore Bootstrap { get; }
    public DataRootBootstrapResolver Resolver { get; }
    public StorageFixture()
    {
        Paths = new(Path.Combine(DirectoryPath, "config"), Path.Combine(DirectoryPath, "install"));
        Bootstrap = new(Paths);
        Resolver = new(Paths, Bootstrap, new SqliteDataRootDatabaseInspector());
    }
    public DataRootDescriptor Descriptor(string path)
    {
        if (Path.IsPathFullyQualified(path) && !Path.GetFullPath(path).StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture descriptor must be isolated.");
        return new(path, Guid.NewGuid(), 1, 1);
    }
    public FinalCheckDbContext OpenDatabase(string database)
    {
        if (!Path.GetFullPath(database).StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture database must be isolated.");
        return new(new DbContextOptionsBuilder<FinalCheckDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()).Options);
    }
    public async Task CreateDatabaseAsync(string database, string? migration = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        await using var context = OpenDatabase(database);
        await context.GetService<IMigrator>().MigrateAsync(migration);
        context.DocumentSnapshots.Add(new StoredDocumentSnapshot { Id = Guid.NewGuid(), SnapshotSchemaVersion = 2,
            Payload = new FinalCheck.Documents.JsonDocumentSnapshotSerializer().Serialize(FinalCheck.Core.Documents.DocumentSnapshot.Empty), CreatedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }
    public void Dispose()
    {
        // Only this explicitly created GUID fixture is ever removed. No normal AppData is resolved.
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
    }
}

internal sealed record FixturePlatformPaths(string ConfigurationDirectory, string InstallDirectory) : IPlatformStoragePaths
{
    public string DefaultDataRoot => Path.Combine(ConfigurationDirectory, "Data");
    public string LegacyDataRoot => ConfigurationDirectory;
}
