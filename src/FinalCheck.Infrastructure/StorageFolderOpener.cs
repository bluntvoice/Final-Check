using System.Diagnostics;
using FinalCheck.Core.Storage;

namespace FinalCheck.Infrastructure;

public sealed class StorageFolderOpener : IStorageFolderOpener
{
    public void Open(string absoluteDirectory)
    {
        if (!Path.IsPathFullyQualified(absoluteDirectory) || !Directory.Exists(absoluteDirectory))
            throw new DirectoryNotFoundException("The active data directory is unavailable.");
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.ArgumentList.Add(absoluteDirectory);
            using var process = Process.Start(start);
        }
        else
        {
            var start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add(absoluteDirectory);
            using var process = Process.Start(start);
        }
    }
}
