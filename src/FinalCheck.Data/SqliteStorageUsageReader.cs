using System.Text.Json;
using FinalCheck.Core.Storage;
using Microsoft.Data.Sqlite;

namespace FinalCheck.Data;

public sealed class SqliteStorageUsageReader : IStorageDatabaseUsageReader
{
    public async Task<StorageDatabaseUsage> ReadAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        async Task<long> Bytes(string table)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // table is a private, fixed application schema name, never user input.
            command.CommandText = $"SELECT COALESCE(SUM(length(Payload)), 0) FROM {table}";
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }
        var snapshots = await Bytes("DocumentSnapshots");
        var comparisons = checked(await Bytes("ComparisonResults") + await Bytes("ComparisonRecords"));
        var restores = checked(await Bytes("RestoredWorkingCopies") + await Bytes("FormatRestoreOperations"));
        var originals = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction; command.CommandText = "SELECT Payload FROM ComparisonRecords";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var record = ComparisonRecordStore.Decode((byte[])reader[0]);
                originals.Add(Path.GetFullPath(record.BaselineFile.Path)); originals.Add(Path.GetFullPath(record.CurrentFile.Path));
            }
        }
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT FilePath FROM TemplateVersions";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) originals.Add(Path.GetFullPath(reader.GetString(0)));
        }
        foreach (var table in new[] { "RestoredWorkingCopies", "FormatRestoreOperations" })
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT Payload FROM {table}";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                using var payload = JsonDocument.Parse((byte[])reader[0]);
                var copy = table == "RestoredWorkingCopies" ? payload.RootElement : payload.RootElement.GetProperty("WorkingCopy");
                var original = copy.GetProperty("OriginalPath").GetString();
                if (original is null || !Path.IsPathFullyQualified(original)) throw new InvalidDataException("Invalid original document reference.");
                originals.Add(Path.GetFullPath(original));
            }
        }
        return new(snapshots, comparisons, restores, originals);
    }
}
