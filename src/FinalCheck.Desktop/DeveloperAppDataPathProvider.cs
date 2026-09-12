#if DEBUG
using FinalCheck.Core.Abstractions;

namespace FinalCheck.Desktop;

/// <summary>Explicit Debug-only isolation for developer smoke checks. Never available in release packages.</summary>
internal sealed class DeveloperAppDataPathProvider : IAppDataPathProvider
{
    private readonly string _directory;
    public DeveloperAppDataPathProvider(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Developer data directory must be absolute.");
        _directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var production = Path.TrimEndingDirectorySeparator(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FinalCheck"));
        if (string.Equals(_directory, production, StringComparison.OrdinalIgnoreCase) ||
            _directory.StartsWith(production + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Developer checks cannot use the normal application data directory.");
    }
    public string GetAppDataDirectory() { Directory.CreateDirectory(_directory); return _directory; }
    public string GetDatabasePath() => Path.Combine(GetAppDataDirectory(), "finalcheck.db");
}
#endif
