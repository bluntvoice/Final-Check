using System.Diagnostics;
using FinalCheck.Core.Storage;

namespace FinalCheck.Infrastructure;

public static class StorageStartupDiagnostics
{
    public static void Write(IPlatformStoragePaths paths, string stage, Exception error)
    {
        try
        {
            var location = Path.Combine(paths.ConfigurationDirectory, "startup-diagnostic.log");
            StorageFileSafety.RejectLinks(location);
            Directory.CreateDirectory(paths.ConfigurationDirectory);
            File.AppendAllText(location, $"{DateTimeOffset.UtcNow:O} {stage} {error.GetType().Name}{Environment.NewLine}");
        }
        catch (Exception diagnosticError) when (diagnosticError is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Storage startup diagnostic unavailable: {diagnosticError.GetType().Name}");
        }
    }
}
