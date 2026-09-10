using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinalCheck.Data;

public sealed class FinalCheckDbContextFactory : IDesignTimeDbContextFactory<FinalCheckDbContext>
{
    public FinalCheckDbContext CreateDbContext(string[] args)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "FinalCheck", "design-time.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var options = new DbContextOptionsBuilder<FinalCheckDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        return new FinalCheckDbContext(options);
    }
}
