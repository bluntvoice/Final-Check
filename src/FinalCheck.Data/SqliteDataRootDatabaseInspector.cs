using FinalCheck.Core.Storage;
using Microsoft.Data.Sqlite;

namespace FinalCheck.Data;

/// <summary>Read-only recognition before EF migrations are permitted. Never creates a missing database.</summary>
public sealed class SqliteDataRootDatabaseInspector : IDataRootDatabaseInspector
{
    internal static readonly string[] KnownMigrations =
    ["20260910021629_InitialCreate", "20260910143000_AddComparisonResults", "20260912110000_AddFormatRestoreHistory", "20260912234643_AddIndependentComparisonRecords", "20260913054718_AddTemplateCenter"];

    public async Task ValidateAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath)) throw new FileNotFoundException("Storage database is missing.", databasePath);
        if ((File.GetAttributes(databasePath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked database files are unsupported.");
        var options = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        await using var connection = new SqliteConnection(options.ToString());
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken) || reader.GetString(0) != "ok" || await reader.ReadAsync(cancellationToken))
                throw new InvalidDataException("SQLite integrity validation failed.");
        }
        command.CommandText = "PRAGMA foreign_key_check";
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            if (await reader.ReadAsync(cancellationToken)) throw new InvalidDataException("SQLite foreign-key validation failed.");
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
        var migrations = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) migrations.Add(reader.GetString(0));
        if (migrations.Count == 0 || migrations.Count > KnownMigrations.Length || !migrations.SequenceEqual(KnownMigrations.Take(migrations.Count)))
            throw new InvalidDataException("Not a supported Final Check migration history.");
        command.CommandText = "SELECT Id, SnapshotSchemaVersion, Payload, CreatedAtUtc FROM DocumentSnapshots LIMIT 0";
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (migrations.Count >= 2)
        {
            command.CommandText = "SELECT Id, ComparisonSchemaVersion, BaselineSnapshotId, CurrentSnapshotId, AlgorithmVersion, Payload, CreatedAtUtc FROM ComparisonResults LIMIT 0";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (migrations.Count >= 3)
        {
            command.CommandText = "SELECT ContractVersionId, Sha256, Payload, UpdatedAtUtc FROM RestoredWorkingCopies LIMIT 0";
            await command.ExecuteNonQueryAsync(cancellationToken);
            command.CommandText = "SELECT Id, ContractVersionId, SchemaVersion, Status, Payload, CreatedAtUtc FROM FormatRestoreOperations LIMIT 0";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (migrations.Count >= 4)
        {
            command.CommandText = "SELECT Id, BaselineSnapshotId, CurrentSnapshotId, ResultId, Payload, CreatedAtUtc FROM ComparisonRecords LIMIT 0";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (migrations.Count >= 5)
        {
            command.CommandText = "SELECT Id, Name, ContractType, IsEnabled, IsDeleted, Notes, CreatedAtUtc, UpdatedAtUtc FROM Templates LIMIT 0";
            await command.ExecuteNonQueryAsync(cancellationToken);
            command.CommandText = "SELECT Id, TemplateId, Version, FilePath, FileName, FileSize, ModifiedAtUtc, Sha256, SnapshotId, IsCurrent, ParseStatus, CreatedAtUtc FROM TemplateVersions LIMIT 0";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
