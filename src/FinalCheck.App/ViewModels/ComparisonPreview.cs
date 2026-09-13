using CommunityToolkit.Mvvm.ComponentModel;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.App.ViewModels;

public sealed record PreviewSegment(string Text, CharacterFormatSnapshot Format, bool Changed);
public sealed record PreviewParagraph(string NodeId, IReadOnlyList<string> NodeIds, IReadOnlyList<PreviewSegment> Segments, string? Alignment);
public sealed record PreviewCell(string NodeId, int Column, int Span, IReadOnlyList<PreviewParagraph> Paragraphs);
public sealed record PreviewBlock(string NodeId, string Label, IReadOnlyList<string> NodeIds, IReadOnlyList<PreviewCell> Cells, bool IsTable);

/// <summary>Immutable snapshot projection, not a Word renderer or another comparison algorithm.</summary>
public static class ComparisonPreviewBuilder
{
    public static IReadOnlyList<PreviewBlock> Build(DocumentSnapshot snapshot, ComparisonResult result, bool baseline)
    {
        var changes = result.Changes.GroupBy(c => baseline ? c.BaselineNodeId ?? "" : c.CurrentNodeId ?? "")
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        PreviewParagraph Paragraph(DocumentParagraphSnapshot paragraph, IReadOnlyList<ComparisonChangeItem>? additional = null)
        {
            var spans = changes.GetValueOrDefault(paragraph.NodeId, []).Concat(additional ?? []).SelectMany(c => c.DifferenceSpans).ToArray();
            var whole = changes.GetValueOrDefault(paragraph.NodeId, []).Any(c => baseline ? c.Kind == ComparisonChangeKind.ParagraphDelete : c.Kind == ComparisonChangeKind.ParagraphInsert);
            var diff = DifferenceHighlight.Build(paragraph.DisplayText, spans, baseline, whole);
            var runs = paragraph.Runs.Where(r => r.DisplayText.Length > 0).ToArray();
            var segments = new List<PreviewSegment>();
            if (string.Concat(runs.Select(r => r.DisplayText)) != paragraph.DisplayText)
                segments.AddRange(diff.Select(s => new PreviewSegment(s.Text, CharacterFormatSnapshot.Empty, s.Changed)));
            else
            {
                var runIndex = 0; var runOffset = 0;
                foreach (var part in diff)
                {
                    var offset = 0;
                    while (offset < part.Text.Length)
                    {
                        var run = runs[runIndex]; var length = Math.Min(part.Text.Length - offset, run.DisplayText.Length - runOffset);
                        segments.Add(new(part.Text.Substring(offset, length), run.EffectiveFormatting, part.Changed));
                        offset += length; runOffset += length;
                        if (runOffset == run.DisplayText.Length) { runIndex++; runOffset = 0; }
                    }
                }
            }
            return new(paragraph.NodeId, ParagraphIds(paragraph).ToArray(), segments, paragraph.EffectiveFormatting.Alignment);
        }
        static IEnumerable<string> ParagraphIds(DocumentParagraphSnapshot p) => new[] { p.NodeId }.Concat(p.Runs.Select(r => r.NodeId));
        var blocks = new List<(int Source, int Row, PreviewBlock Block)>();
        foreach (var p in snapshot.Paragraphs)
            blocks.Add((p.Identity.SourceIndex, -1, new(p.NodeId, $"正文第 {p.Index + 1} 段", ParagraphIds(p).ToArray(), [new(p.NodeId, 0, 1, [Paragraph(p)])], false)));
        foreach (var table in snapshot.Tables)
        {
            foreach (var row in table.Rows)
            {
                var ids = new[] { row.NodeId }.Concat(row.Index == 0 ? new[] { table.NodeId } : [])
                    .Concat(row.Cells.SelectMany(c => new[] { c.NodeId }.Concat(c.Paragraphs.SelectMany(ParagraphIds)))).ToArray();
                var cells = row.Cells.Select(c => new PreviewCell(c.NodeId, c.ColumnIndex, Math.Max(1, c.DirectFormatting.GridSpan ?? 1),
                    c.Paragraphs.Select(p => Paragraph(p, c.Paragraphs.Count == 1 && c.DisplayText == p.DisplayText ? changes.GetValueOrDefault(c.NodeId, []) : null)).ToArray())).ToArray();
                blocks.Add((table.Identity.SourceIndex, row.Index, new(row.NodeId, $"表格 {table.Index + 1} · 第 {row.Index + 1} 行", ids, cells, true)));
            }
            if (table.Rows.Count == 0) blocks.Add((table.Identity.SourceIndex, 0, new(table.NodeId, $"表格 {table.Index + 1}（无可预览行）", [table.NodeId], [], true)));
        }
        return blocks.OrderBy(b => b.Source).ThenBy(b => b.Row).Select(b => b.Block).ToArray();
    }
}

public sealed partial class ComparisonPreviewViewModel : ViewModelBase
{
    public IReadOnlyList<PreviewBlock> Baseline { get; }
    public IReadOnlyList<PreviewBlock> Current { get; }
    private readonly Dictionary<string, int> baselineIndex;
    private readonly Dictionary<string, int> currentIndex;
    private readonly Dictionary<string, string> toCurrent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> toBaseline = new(StringComparer.Ordinal);
    private bool lastBaseline = true;
    private int lastTop;
    [ObservableProperty] private bool linked = true;
    [ObservableProperty] private PreviewBlock? locatedBaseline;
    [ObservableProperty] private PreviewBlock? locatedCurrent;
    [ObservableProperty] private string? baselineTarget;
    [ObservableProperty] private string? currentTarget;
    [ObservableProperty] private string notice = "基于快照的内容预览，不是 Word 页码/排版；删除修订内容见修改详情。";
    public event Action<bool, int>? ScrollRequested;
    public ComparisonPreviewViewModel(ComparisonWorkflowResult outcome)
    {
        Baseline = ComparisonPreviewBuilder.Build(outcome.Baseline, outcome.Result, true);
        Current = ComparisonPreviewBuilder.Build(outcome.Current, outcome.Result, false);
        baselineIndex = Index(Baseline); currentIndex = Index(Current);
        foreach (var mapping in outcome.Result.NodeMappings.Where(m => m.Confidence != ComparisonConfidenceLevel.Low))
        {
            toCurrent.TryAdd(mapping.BaselineNode.NodeId, mapping.CurrentNode.NodeId);
            toBaseline.TryAdd(mapping.CurrentNode.NodeId, mapping.BaselineNode.NodeId);
        }
    }
    private static Dictionary<string, int> Index(IReadOnlyList<PreviewBlock> blocks)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < blocks.Count; i++) foreach (var id in blocks[i].NodeIds) index.TryAdd(id, i);
        return index;
    }
    public void Locate(ComparisonChangeItem? change)
    {
        BaselineTarget = change?.BaselineNodeId; CurrentTarget = change?.CurrentNodeId;
        LocatedBaseline = LocateSide(true, BaselineTarget); LocatedCurrent = LocateSide(false, CurrentTarget);
        Notice = change is null ? "请选择修改以定位上下文。" : LocatedBaseline is null && LocatedCurrent is null ? "该修改暂无法在正文/表格预览定位；请查看详情与诊断。" :
            change.MatchConfidence is ComparisonConfidenceLevel.Low or ComparisonConfidenceLevel.Medium ? "匹配可信度较低，定位仅供参考，请人工确认。" : "已定位修改上下文；预览不提供 Word 页码或高保真排版。";
    }
    private PreviewBlock? LocateSide(bool baseline, string? id)
    {
        var index = baseline ? baselineIndex : currentIndex;
        if (id is null || !index.TryGetValue(id, out var position)) return null;
        ScrollRequested?.Invoke(baseline, position); return (baseline ? Baseline : Current)[position];
    }
    public void ViewportMoved(bool baseline, int firstVisible)
    {
        var blocks = baseline ? Baseline : Current;
        if (firstVisible < 0 || firstVisible >= blocks.Count) return;
        lastBaseline = baseline; lastTop = firstVisible;
        if (Linked) Align(baseline, firstVisible);
    }
    partial void OnLinkedChanged(bool value) { if (value) Align(lastBaseline, lastTop); }
    private void Align(bool baseline, int position)
    {
        var blocks = baseline ? Baseline : Current; if (position < 0 || position >= blocks.Count) return;
        var mappings = baseline ? toCurrent : toBaseline; var target = baseline ? currentIndex : baselineIndex;
        // Use a nearby matched logical node, never a scroll-offset percentage. No low-confidence guesses.
        for (var distance = 0; distance <= 2; distance++)
        {
            foreach (var source in new[] { position + distance, position - distance }.Distinct())
            {
                if (source < 0 || source >= blocks.Count) continue;
                foreach (var id in blocks[source].NodeIds)
                    if (mappings.TryGetValue(id, out var partner) && target.TryGetValue(partner, out var index))
                    { ScrollRequested?.Invoke(!baseline, index); return; }
            }
        }
        Notice = "当前位置附近无可靠匹配节点，另一侧保持不动。";
    }
}
