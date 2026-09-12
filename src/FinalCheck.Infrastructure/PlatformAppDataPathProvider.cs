using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Storage;

namespace FinalCheck.Infrastructure;

/// <summary>Compatibility adapter; the bootstrap-resolved provider is the only path authority.</summary>
public sealed class PlatformAppDataPathProvider(IDataRootProvider provider) : IAppDataPathProvider
{
    public string GetAppDataDirectory()
    {
        var path = provider.CurrentDataRoot;
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetDatabasePath() => provider.DatabasePath;
}
