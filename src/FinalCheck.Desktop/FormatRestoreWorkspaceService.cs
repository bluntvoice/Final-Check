using System.Security.Cryptography;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Formatting;
using FinalCheck.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Desktop;

public sealed class FormatRestoreWorkspaceService(IServiceScopeFactory scopes, IFormatRestorePlanner planner,
    IWorkingCopyComparisonService comparisons) : IFormatRestoreWorkspaceService
{
    public Task<FormatRestoreAnalysis> AnalyzeAsync(Guid comparisonRecordId, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var outcome = await services.GetRequiredService<IComparisonRecordStore>().LoadAsync(comparisonRecordId, cancellationToken);
            if (outcome is null || outcome.IsPartial)
                return Block(FormatRestoreAnalysisStatus.Unavailable, "比对不存在或文档解析不完整，不能生成可靠恢复计划。");
            var db = services.GetRequiredService<FinalCheckDbContext>();
            var link = await db.ProjectComparisons.AsNoTracking().SingleOrDefaultAsync(x => x.RecordId == comparisonRecordId, cancellationToken);
            if (link is null)
                return Block(FormatRestoreAnalysisStatus.Unavailable, "此历史记录缺少合同版本关联，请从项目版本重新比对。");
            var version = await db.ContractVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == link.CurrentVersionId, cancellationToken);
            if (version is null || version.SnapshotId != link.CurrentSourceSnapshotId || version.Sha256 != outcome.Current.Metadata.Sha256)
                return Block(FormatRestoreAnalysisStatus.Unavailable, "合同版本与历史快照不一致，请重新打开项目检查。");
            var store = services.GetRequiredService<IFormatRestoreStore>();
            if ((await store.LoadPendingAsync(link.CurrentVersionId, cancellationToken)).Count > 0)
                return Block(FormatRestoreAnalysisStatus.RecoveryRequired, "存在未完成恢复操作，请正常关闭并重启核验。文件与历史仍保留。", link.CurrentVersionId);
            var copy = await store.LoadWorkingCopyAsync(link.CurrentVersionId, cancellationToken);
            if (copy is not null)
            {
                var expected = Path.Combine(services.GetRequiredService<IAppDataPathProvider>().GetAppDataDirectory(),
                    "WorkingCopies", link.CurrentVersionId.ToString("N"), "restored.docx");
                if (!Path.GetFullPath(copy.WorkingPath).Equals(Path.GetFullPath(expected), PathComparison))
                    return Block(FormatRestoreAnalysisStatus.RecoveryRequired, "工作文件位置与版本身份不一致，请核验存储状态。", link.CurrentVersionId);
                if (copy.Snapshot.ParseStatus != DocumentParseStatus.Complete)
                    return Block(FormatRestoreAnalysisStatus.Unavailable, "工作文件解析不完整，不能生成可靠恢复计划。", link.CurrentVersionId);
            }
            var source = copy?.WorkingPath ?? version.FilePath;
            var expectedHash = copy?.Sha256 ?? outcome.Current.Metadata.Sha256;
            if (!await MatchesAsync(source, expectedHash, cancellationToken))
                return Block(copy is null ? FormatRestoreAnalysisStatus.SourceChanged : FormatRestoreAnalysisStatus.WorkingCopyChanged,
                    copy is null ? "原始修订文件缺失或已变化，请重新比对当前文件。" : "格式恢复版缺失或已被外部修改，请先选择保留修改或重新生成。",
                    link.CurrentVersionId, copy);
            // Keep the saved comparison unchanged. A working copy needs fresh mappings against the same historical baseline.
            var current = copy?.Snapshot ?? outcome.Current;
            var comparison = copy is null ? outcome.Result : comparisons.Compare(outcome.Baseline, current, cancellationToken);
            var plan = planner.Generate(outcome.Baseline, current, comparison, cancellationToken: cancellationToken);
            return new FormatRestoreAnalysis(FormatRestoreAnalysisStatus.Ready, link.CurrentVersionId,
                outcome with { Current = current, Result = comparison }, plan, copy, "计划已生成；分析没有写入 DOCX 或恢复历史。") { SourcePath = source };
        }, cancellationToken);

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static FormatRestoreAnalysis Block(FormatRestoreAnalysisStatus status, string message, Guid? version = null, RestoredWorkingCopy? copy = null) =>
        new(status, version, null, null, copy, message);
    private static async Task<bool> MatchesAsync(string path, string expectedHash, CancellationToken token)
    {
        if (!File.Exists(path)) return false;
        // No write handles, directory creation, recovery or source reparsing during analysis.
        for (var cursor = Path.GetFullPath(path); cursor is not null; cursor = Path.GetDirectoryName(cursor))
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
