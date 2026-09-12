using FinalCheck.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data;

public sealed class DataRootDbContextFactory(IDataRootProvider provider, IStorageMaintenanceCoordinator? maintenance = null)
{
    public FinalCheckDbContext CreateDbContext()
    {
        var session = maintenance?.OpenSession();
        try
        {
            var paths = new DataRootPaths((session ?? provider).Descriptor);
            var connection = new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Pooling = false };
            return new(new DbContextOptionsBuilder<FinalCheckDbContext>().UseSqlite(connection.ToString()).Options, session);
        }
        catch { session?.Dispose(); throw; }
    }
}
