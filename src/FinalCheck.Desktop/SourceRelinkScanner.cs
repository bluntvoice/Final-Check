using FinalCheck.Core.Comparisons;

namespace FinalCheck.Desktop;

internal static class SourceRelinkScanner
{
    public static Task<IReadOnlyList<ComparisonFile>> FindAsync(IComparisonFileInspector files, ComparisonFile source, CancellationToken token) => Task.Run(async () =>
    {
        var root = Path.GetDirectoryName(source.Path); var matches = new List<ComparisonFile>();
        if (root is null || !Directory.Exists(root)) return (IReadOnlyList<ComparisonFile>)matches;
        var directories = new[] { root }.Concat(Directory.EnumerateDirectories(root).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).Take(20));
        foreach (var candidate in directories.SelectMany(p => Directory.EnumerateFiles(p, "*.docx")).Take(500))
        {
            token.ThrowIfCancellationRequested();
            try { if (new FileInfo(candidate).Length != source.Size) continue; var file = await files.InspectAsync(candidate, token);
                if (file.Sha256.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase)) matches.Add(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return matches.OrderByDescending(f => f.Name == source.Name).ThenBy(f => f.Path, StringComparer.Ordinal).ToArray();
    }, token);
}
