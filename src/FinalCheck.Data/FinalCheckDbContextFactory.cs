using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using FinalCheck.Core.Storage;

namespace FinalCheck.Data;

public sealed class FinalCheckDbContextFactory : IDesignTimeDbContextFactory<FinalCheckDbContext>
{
    public FinalCheckDbContext CreateDbContext(string[] args)
    {
        // Tool-only isolated root. Never resolve normal user's bootstrap or mutate their database.
        var index = Array.IndexOf(args, "--data-root");
        if (index >= 0 && index + 1 >= args.Length) throw new ArgumentException("--data-root requires an absolute tool-only directory.");
        var root = index >= 0 ? args[index + 1] : Path.Combine(Path.GetTempPath(), "FinalCheck.DesignTime");
        var paths = new DataRootPaths(new(root, Guid.NewGuid(), DataRootPaths.CurrentLayoutVersion, 1));
        Directory.CreateDirectory(paths.CurrentDataRoot);
        return new DataRootDbContextFactory(paths).CreateDbContext();
    }
}
