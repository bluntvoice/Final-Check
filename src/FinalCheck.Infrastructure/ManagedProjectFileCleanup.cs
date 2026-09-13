using System.Security.Cryptography;
using FinalCheck.Core.Management;
using FinalCheck.Core.Storage;

namespace FinalCheck.Infrastructure;
/// <summary>Exact journal-owned files only; no recursive delete and no source document deletion.</summary>
public static class ManagedProjectFileCleanup
{
    public static IDisposable LockVersions(IDataRootProvider paths, IReadOnlyList<Guid> versionIds)
    {
        var locks = new LockSet();
        try
        {
            foreach (var id in versionIds.Order())
            {
                if (id == Guid.Empty) throw new InvalidDataException("Empty version identity."); var directory = Path.Combine(paths.WorkingCopyPath, id.ToString("N")); RejectLinks(directory); Directory.CreateDirectory(directory);
                locks.Streams.Add(new FileStream(Path.Combine(directory, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            return locks;
        }
        catch { locks.Dispose(); throw; }
    }
    private sealed class LockSet : IDisposable
    {
        public List<FileStream> Streams { get; } = [];
        public void Dispose() { foreach (var stream in Streams) stream.Dispose(); }
    }
    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked cleanup paths are unsafe.");
    }
    private static async Task<string> HashAsync(string path)
    { await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Convert.ToHexString(await SHA256.HashDataAsync(stream)); }
    public static async Task<ProjectDeletionRecord> RunAsync(ProjectDeletionRecord record, IDataRootProvider paths)
    {
        try
        {
            var originals = record.OriginalPaths.Select(Path.GetFullPath).ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var file in record.Files)
            {
                var parts = file.RelativePath.Replace('\\', '/').Split('/');
                if (parts.Length != 3 || parts[0] != "WorkingCopies" || !Guid.TryParseExact(parts[1], "N", out var version) || !record.VersionIds.Contains(version) ||
                    file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit) || !AllowedName(parts[2])) throw new InvalidDataException("Invalid journal-owned cleanup file.");
                var source = Path.GetFullPath(Path.Combine(paths.CurrentDataRoot, file.RelativePath));
                var staged = Path.Combine(paths.BackupPath, "ProjectDeletion", record.OperationId.ToString("N"), parts[1], parts[2]);
                RejectLinks(source); RejectLinks(staged);
                if (originals.Contains(source) || originals.Contains(Path.GetFullPath(staged))) throw new InvalidDataException("Original DOCX is protected.");
                if (File.Exists(source))
                {
                    if (File.Exists(staged) || !file.Sha256.Equals(await HashAsync(source), StringComparison.OrdinalIgnoreCase)) throw new IOException("Managed file changed; retained for review.");
                    Directory.CreateDirectory(Path.GetDirectoryName(staged)!); File.Move(source, staged, false);
                }
                if (!File.Exists(staged)) continue; // Already cleaned: safe retry after interruption.
                if (!file.Sha256.Equals(await HashAsync(staged), StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(source)) File.Move(staged, source, false);
                    throw new IOException("Staged hash differs; changed data retained.");
                }
                File.Delete(staged); // Exact verified, engine-owned staging path; never a broad directory.
            }
            foreach (var id in record.VersionIds)
            {
                var directory = Path.Combine(paths.WorkingCopyPath, id.ToString("N")); RejectLinks(directory);
                if (Directory.Exists(directory) && (Directory.EnumerateFiles(directory).Any(p => Path.GetFileName(p) != "operation.lock") || Directory.EnumerateDirectories(directory).Any()))
                    return record with { Status = "NeedsReview", Diagnostic = "项目记录已永久删除；存在未追踪/变更的恢复文件，安全保留，未自动删除。" };
            }
            return record with { Status = "Completed", Diagnostic = "仅清理已验证的独占托管文件；外部 DOCX 与共享资源保留。" };
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { return record with { Status = "NeedsReview", Diagnostic = "项目记录已永久删除，文件清理尚未完成并可重试：" + e.Message }; }
    }
    private static bool AllowedName(string name) => name == "restored.docx" ||
        name.EndsWith(".docx", StringComparison.Ordinal) && (name.StartsWith("previous-", StringComparison.Ordinal) && Guid.TryParseExact(name[9..^5], "N", out _) || name.StartsWith("candidate-", StringComparison.Ordinal) && Guid.TryParseExact(name[10..^5], "N", out _));
}
