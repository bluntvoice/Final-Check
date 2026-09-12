using System.Security.Cryptography;
using FinalCheck.Infrastructure;

namespace FinalCheck.Data.Tests;

public sealed class ComparisonInputTests
{
    [Fact] public async Task InspectorReadsMetadataHashWithoutCopyingOrChangingOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(), "FinalCheck.Comparison.Input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "输入.DOCX"); var bytes = new byte[] { 1, 2, 3, 4 };
            await File.WriteAllBytesAsync(path, bytes);
            var actual = await new ComparisonFileInspector().InspectAsync(path);
            Assert.Equal(Path.GetFullPath(path), actual.Path); Assert.Equal(4, actual.Size);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), actual.Sha256);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path)); Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact] public async Task InspectorRejectsUnsupportedMissingAndCancelledInputs()
    {
        var service = new ComparisonFileInspector();
        await Assert.ThrowsAsync<ArgumentException>(() => service.InspectAsync("unsupported.pdf"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.InspectAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InspectAsync("cancelled.docx", new CancellationToken(true)));
    }
}
