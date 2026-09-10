namespace FinalCheck.Core.Abstractions;

public interface IAppDataPathProvider
{
    string GetAppDataDirectory();

    string GetDatabasePath();
}

public interface IFileHashService
{
    ValueTask<string> ComputeSha256Async(
        Stream stream,
        CancellationToken cancellationToken = default);
}

public interface IUpdateService
{
    ValueTask<UpdateCheckResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default);
}

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string? Version,
    string? ReleaseNotes);
