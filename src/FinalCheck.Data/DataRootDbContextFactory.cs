using FinalCheck.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data;

public sealed class DataRootDbContextFactory(IDataRootProvider provider)
{
    public FinalCheckDbContext CreateDbContext()
    {
        var paths = new DataRootPaths(provider.Descriptor);
        var connection = new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Pooling = false };
        return new(new DbContextOptionsBuilder<FinalCheckDbContext>().UseSqlite(connection.ToString()).Options);
    }
}
