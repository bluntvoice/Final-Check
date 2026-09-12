using FinalCheck.Core.Storage;

namespace FinalCheck.Infrastructure;

public sealed record StorageRecoveryDiagnostic(Guid? MigrationId, string Code, string? RetainedPath);

public sealed class StorageMigrationRecoveryService(IDataRootProvider provider, IPlatformStoragePaths platform,
    IStorageBootstrapStore bootstrap, IStorageMaintenanceCoordinator coordinator,
    IStorageDatabaseMigrationService databases, StorageMigrationJournal journal)
{
    public async Task<IReadOnlyList<StorageRecoveryDiagnostic>> RecoverAsync(CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<StorageRecoveryDiagnostic>();
        if (Directory.Exists(platform.ConfigurationDirectory) && Directory.EnumerateFiles(platform.ConfigurationDirectory, "*.tmp").Any())
            diagnostics.Add(new(null, "UnpublishedControlTemporaryFilesRetained", platform.ConfigurationDirectory));
        var pending = journal.Load().Where(record => record.Status == StorageMigrationStatus.InProgress).ToArray();
        if (pending.Length == 0) return diagnostics;
        using var maintenance = await coordinator.EnterMaintenanceAsync(cancellationToken);
        try
        {
        var locator = bootstrap.Load() ?? throw new InvalidDataException("Recovery needs a valid locator.");
        foreach (var record in pending)
        {
            if (locator.Current == record.Target && record.DataValidated && record.Stage == StorageMigrationStage.Committing)
            {
                // A durable committed root may already contain newer legitimate writes. Validate it in place;
                // never compare it with, or restore it from, the stale source migration snapshot.
                await using var database = await databases.OpenSourceAsync(provider.CurrentDataRoot, cancellationToken);
                await database.ValidateReopenedAsync(provider.CurrentDataRoot, cancellationToken);
                journal.Save(record with { Status = StorageMigrationStatus.Completed, Stage = StorageMigrationStage.Completed, CompletedAt = DateTimeOffset.UtcNow });
                diagnostics.Add(new(record.MigrationId, "CommittedMigrationReceiptRecovered", record.Source.Path));
            }
            else if (locator.Current == record.Source)
                diagnostics.Add(new(record.MigrationId, "IncompleteMigrationOldRootActive", Directory.Exists(record.StagingPath) ? record.StagingPath : record.Target.Path));
            else
            {
                maintenance.RequireRecovery();
                throw new InvalidDataException("Migration journal and locator disagree; data writes are blocked.");
            }
        }
        maintenance.Complete();
        return diagnostics;
        }
        catch { maintenance.RequireRecovery(); throw; }
    }
}
