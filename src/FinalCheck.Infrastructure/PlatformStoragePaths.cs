using FinalCheck.Core.Storage;

namespace FinalCheck.Infrastructure;

public sealed class PlatformStoragePaths(string? installDirectory = null) : IPlatformStoragePaths
{
    public string ConfigurationDirectory
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) throw new InvalidOperationException("Local application data is unavailable.");
            return Path.Combine(local, "FinalCheck");
        }
    }
    public string DefaultDataRoot => Path.Combine(ConfigurationDirectory, "Data");
    public string LegacyDataRoot => ConfigurationDirectory;
    public string InstallDirectory => Path.GetFullPath(installDirectory ?? AppContext.BaseDirectory);
}
