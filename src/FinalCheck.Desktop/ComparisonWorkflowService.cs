using System.Security.Cryptography;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Comparison;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Desktop;

/// <summary>Application orchestration at the composition boundary; App sees only Core contracts.</summary>
public sealed class ComparisonWorkflowService(IComparisonFileInspector files, IDocumentParser parser,
    IComparisonEngine engine, IServiceScopeFactory scopes) : IComparisonWorkflowService
{
    public async Task<ComparisonInputValidation> ValidateAsync(ComparisonFile baseline, ComparisonFile current, CancellationToken cancellationToken = default)
    {
        var left = await files.InspectAsync(baseline.Path, cancellationToken);
        var right = await files.InspectAsync(current.Path, cancellationToken);
        return new(left, right, string.Equals(left.Path, right.Path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
            left.Sha256 == right.Sha256, left.Sha256 != baseline.Sha256 || right.Sha256 != current.Sha256);
    }
    public Task<ComparisonWorkflowResult> ExecuteAsync(ComparisonInputValidation input, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            // Read handles protect against cooperative writers through parse + comparison; no source bytes are copied to DataRoot.
            await using var left = Open(input.Baseline.Path); await using var right = Open(input.Current.Path);
            progress?.Report("正在校验文件…");
            await VerifyAsync(left, input.Baseline.Sha256, cancellationToken);
            await VerifyAsync(right, input.Current.Sha256, cancellationToken);
            progress?.Report("正在读取基准文档…");
            var baseline = await parser.ParseAsync(left, cancellationToken: cancellationToken);
            progress?.Report("正在读取当前文档…");
            var current = await parser.ParseAsync(right, cancellationToken: cancellationToken);
            if (baseline.Metadata.Sha256 != input.Baseline.Sha256 || current.Metadata.Sha256 != input.Current.Sha256)
                throw new IOException("文件在读取期间发生变化，请重新选择后重试。");
            var result = engine.Compare(baseline, current, new StageProgress(progress), cancellationToken);
            await VerifyAsync(left, input.Baseline.Sha256, cancellationToken); await VerifyAsync(right, input.Current.Sha256, cancellationToken);
            progress?.Report("正在保存比对结果…");
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IComparisonRecordStore>().SaveAsync(input.Baseline, input.Current, baseline, current, result, cancellationToken);
        }, cancellationToken);
    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
    private static async Task VerifyAsync(Stream stream, string expected, CancellationToken token)
    {
        stream.Position = 0;
        if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("文件内容已发生变化，请刷新文件后重试。");
        stream.Position = 0;
    }
    private sealed class StageProgress(IProgress<string>? target) : IProgress<ComparisonProgress>
    {
        public void Report(ComparisonProgress value) => target?.Report(value.Stage switch
        {
            ComparisonStage.MatchingStructure => "正在匹配文档结构…", ComparisonStage.DetectingMoves => "正在识别段落移动…",
            ComparisonStage.ComparingText => "正在比较文字…", ComparisonStage.ComparingFormatting => "正在比较格式…",
            ComparisonStage.ProcessingRevisionsAndComments => "正在处理修订和批注…", ComparisonStage.GroupingChanges => "正在整理修改结果…",
            _ => "正在准备比对…",
        });
    }
    public Task<IReadOnlyList<ComparisonRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    { await using var scope = scopes.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<IComparisonRecordStore>().ListAsync(cancellationToken); }, cancellationToken);
    public Task<ComparisonWorkflowResult?> LoadAsync(Guid recordId, CancellationToken cancellationToken = default) => Task.Run(async () =>
    { await using var scope = scopes.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<IComparisonRecordStore>().LoadAsync(recordId, cancellationToken); }, cancellationToken);
    public Task<ComparisonRecord> UpdateReviewAsync(Guid recordId, IReadOnlyList<string> changeIds, ComparisonReviewState state, CancellationToken cancellationToken = default) => Task.Run(async () =>
    { await using var scope = scopes.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<IComparisonRecordStore>().UpdateReviewAsync(recordId, changeIds, state, cancellationToken); }, cancellationToken);
}
