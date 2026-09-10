using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data;

public sealed class FinalCheckDatabaseInitializer(FinalCheckDbContext dbContext)
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        dbContext.Database.MigrateAsync(cancellationToken);
}
