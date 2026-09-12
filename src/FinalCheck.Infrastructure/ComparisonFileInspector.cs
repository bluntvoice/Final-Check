using System.Security.Cryptography;
using FinalCheck.Core.Comparisons;

namespace FinalCheck.Infrastructure;

public sealed class ComparisonFileInspector : IComparisonFileInspector
{
    public Task<ComparisonFile> InspectAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            if (!string.Equals(System.IO.Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("只支持 DOCX 文件。");
            var fullPath = System.IO.Path.GetFullPath(path);
            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            var info = new FileInfo(fullPath);
            return new ComparisonFile(fullPath, info.Name, stream.Length, info.LastWriteTimeUtc, hash);
        }, cancellationToken);
}
