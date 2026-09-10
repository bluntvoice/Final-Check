using FinalCheck.Core.Abstractions;

namespace FinalCheck.Infrastructure;

public sealed class PlatformAppDataPathProvider : IAppDataPathProvider
{
    public string GetAppDataDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("The operating system did not provide a local application data directory.");
        }

        var path = Path.Combine(root, "FinalCheck");
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetDatabasePath() => Path.Combine(GetAppDataDirectory(), "finalcheck.db");
}
