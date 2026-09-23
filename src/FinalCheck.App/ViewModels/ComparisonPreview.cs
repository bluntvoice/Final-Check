using CommunityToolkit.Mvvm.ComponentModel;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.App.ViewModels;

public sealed record PreviewSegment(string Text, CharacterFormatSnapshot Format, bool Changed);
public sealed record PreviewParagraph(string NodeId, IReadOnlyList<string> NodeIds, IReadOnlyList<PreviewSegment> Segments, string? Alignment);
public sealed record PreviewCell(string NodeId, int Column, int Span, IReadOnlyList<PreviewParagraph> Paragraphs, string? SourceText = null);
public sealed record PreviewBlock(string NodeId, string Label, IReadOnlyList<string> NodeIds, IReadOnlyList<PreviewCell> Cells, bool IsTable);
public sealed record ContextDocumentPanel(DocumentPanelId Source, string Label, IReadOnlyList<PreviewBlock> Blocks,
    string? TargetNodeId, string Notice);

/// <summary>Immutable snapshot projection, not a Word renderer or another comparison algorithm.</summary>
public static class ComparisonPreviewBuilder
{
    public static IReadOnlyList<PreviewBlock> Build(DocumentSnapshot snapshot, ComparisonResult? result = null, bool baseline = false)
    {
        var changes = (result?.Changes ?? []).GroupBy(c => baseline ? c.BaselineNodeId ?? "" : c.CurrentNodeId ?? "")
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
            blocks.Add((p.Identity.SourceIndex, -1, new(p.NodeId, $"正文第 {p.Index + 1} 段", ParagraphIds(p).ToArray(), [new(p.NodeId, 0, 1, [Paragraph(p)], p.DisplayText)], false)));
        foreach (var table in snapshot.Tables)
        {
            foreach (var row in table.Rows)
            {
                var ids = new[] { row.NodeId }.Concat(row.Index == 0 ? new[] { table.NodeId } : [])
                    .Concat(row.Cells.SelectMany(c => new[] { c.NodeId }.Concat(c.Paragraphs.SelectMany(ParagraphIds)))).ToArray();
                var cells = row.Cells.Select(c => new PreviewCell(c.NodeId, c.ColumnIndex, Math.Max(1, c.DirectFormatting.GridSpan ?? 1),
                    c.Paragraphs.Select(p => Paragraph(p, c.Paragraphs.Count == 1 && c.DisplayText == p.DisplayText ? changes.GetValueOrDefault(c.NodeId, []) : null)).ToArray(), c.DisplayText)).ToArray();
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
    public static DocumentPanelId BaselinePanel { get; } = new("baseline");
    public static DocumentPanelId CurrentPanel { get; } = new("current");
    public IReadOnlyList<WorkspaceDocumentPanel> Panels { get; }
    public LogicalScrollCoordinator Scrolling { get; }
    [ObservableProperty] private bool linked = true;
    [ObservableProperty] private PreviewBlock? locatedBaseline;
    [ObservableProperty] private PreviewBlock? locatedCurrent;
    [ObservableProperty] private ContextDocumentPanel baselineContext = new(BaselinePanel, "基准版本上下文", [], null, "请选择修改项。");
    [ObservableProperty] private ContextDocumentPanel currentContext = new(CurrentPanel, "当前版本上下文", [], null, "请选择修改项。");
    [ObservableProperty] private string? baselineTarget;
    [ObservableProperty] private string? currentTarget;
    [ObservableProperty] private string notice = "基于快照的内容预览，不是 Word 页码/排版；删除修订内容见修改详情。";
    public event Action<bool, int>? ScrollRequested;
    public event Action? NavigationStarted;
    public ComparisonPreviewViewModel(ComparisonWorkflowResult outcome)
    {
        Baseline = ComparisonPreviewBuilder.Build(outcome.Baseline, outcome.Result, true);
        Current = ComparisonPreviewBuilder.Build(outcome.Current, outcome.Result, false);
        baselineIndex = Index(Baseline); currentIndex = Index(Current);
        Panels = [new(BaselinePanel, "基准版本", Baseline), new(CurrentPanel, "当前版本", Current)];
        var mappings = outcome.Result.NodeMappings.Where(m => m.Confidence == ComparisonConfidenceLevel.High && m.Score >= .8)
            .SelectMany(m => new[] { new LogicalNodeLink(BaselinePanel, m.BaselineNode.NodeId, CurrentPanel, m.CurrentNode.NodeId),
                new LogicalNodeLink(CurrentPanel, m.CurrentNode.NodeId, BaselinePanel, m.BaselineNode.NodeId) }).ToArray();
        Scrolling = new(Panels, mappings);
        Scrolling.ScrollRequested += request => ScrollRequested?.Invoke(request.Panel == BaselinePanel, request.BlockIndex);
        Scrolling.Unmatched += () => Notice = "当前位置附近无可靠匹配节点，另一侧保持不动。";
    }
    private static Dictionary<string, int> Index(IReadOnlyList<PreviewBlock> blocks)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < blocks.Count; i++) foreach (var id in blocks[i].NodeIds) index.TryAdd(id, i);
        return index;
    }
    public void Locate(ComparisonChangeItem? change)
    {
        Scrolling.ResetNavigation(); NavigationStarted?.Invoke();
        BaselineTarget = change?.BaselineNodeId; CurrentTarget = change?.CurrentNodeId;
        LocatedBaseline = LocateSide(true, BaselineTarget); LocatedCurrent = LocateSide(false, CurrentTarget);
        BaselineContext = Context(BaselinePanel, "基准版本上下文", Baseline, baselineIndex, BaselineTarget, change, true);
        CurrentContext = Context(CurrentPanel, "当前版本上下文", Current, currentIndex, CurrentTarget, change, false);
        Notice = change is null ? "请选择修改以定位上下文。" : LocatedBaseline is null && LocatedCurrent is null ? "该修改暂无法在正文/表格预览定位；请查看详情与诊断。" :
            change.MatchConfidence is ComparisonConfidenceLevel.Low or ComparisonConfidenceLevel.Medium ? "匹配可信度较低，定位仅供参考，请人工确认。" : "已定位修改上下文；预览不提供 Word 页码或高保真排版。";
    }
    private PreviewBlock? LocateSide(bool baseline, string? id)
    {
        var index = baseline ? baselineIndex : currentIndex;
        if (id is null || !index.TryGetValue(id, out var position)) return null;
        ScrollRequested?.Invoke(baseline, position); return (baseline ? Baseline : Current)[position];
    }
    private static ContextDocumentPanel Context(DocumentPanelId source, string label, IReadOnlyList<PreviewBlock> blocks,
        Dictionary<string, int> index, string? target, ComparisonChangeItem? change, bool baseline)
    {
        if (change is null) return new(source, label, [], null, "请选择修改项。");
        if (target is null) return new(source, label, [], null, "本侧无对应正文；该变化可能是新增、删除或未匹配节点，请查看另一侧及诊断。");
        if (!index.TryGetValue(target, out var center)) return new(source, label, [], target, "该节点无法在已解析正文/表格中定位；请查看详情与诊断。");
        var first = Math.Max(0, center - 1); var last = Math.Min(blocks.Count - 1, center + 1);
        if (blocks[center].IsTable)
        {
            var tableName = blocks[center].Label.Split('·')[0];
            while (first > 0 && center - first < 2 && blocks[first - 1].IsTable && blocks[first - 1].Label.StartsWith(tableName, StringComparison.Ordinal)) first--;
            while (last + 1 < blocks.Count && last - center < 2 && blocks[last + 1].IsTable && blocks[last + 1].Label.StartsWith(tableName, StringComparison.Ordinal)) last++;
        }
        var contextBlocks = new List<PreviewBlock>(last - first + 1);
        for (var i = first; i <= last; i++) contextBlocks.Add(HighlightSelected(blocks[i], i == center ? change : null, target, baseline));
        var notice = change.MatchConfidence is ComparisonConfidenceLevel.Medium or ComparisonConfidenceLevel.Low
            ? "映射可信度较低，请核对上下文。"
            : change.Kind is ComparisonChangeKind.ParagraphMove or ComparisonChangeKind.ParagraphMoveAndModify
                ? baseline ? "段落移动的原位置；黄色节点标记来源，不代表原文被修改。" : "段落移动的现位置；黄色节点标记目标，不代表现文被修改。"
                : "当前修改与必要前后逻辑节点；可展开全文查看更远内容。";
        return new(source, label, contextBlocks, target, notice);
    }
    private static PreviewBlock HighlightSelected(PreviewBlock block, ComparisonChangeItem? change, string target, bool baseline)
    {
        var cells = block.Cells.Select(cell => HighlightCell(block, cell, change, target, baseline)).ToArray();
        return block with { Cells = cells };
    }
    private static PreviewCell HighlightCell(PreviewBlock block, PreviewCell cell, ComparisonChangeItem? change, string target, bool baseline)
    {
        var mappedMultiParagraphCell = cell.NodeId == target && cell.Paragraphs.Count > 1 &&
            cell.SourceText == string.Join("\n", cell.Paragraphs.Select(paragraph => string.Concat(paragraph.Segments.Select(segment => segment.Text))));
        var paragraphs = new List<PreviewParagraph>(cell.Paragraphs.Count); var offset = 0;
        foreach (var paragraph in cell.Paragraphs)
        {
            var textLength = paragraph.Segments.Sum(segment => segment.Text.Length);
            var selected = change is not null && (block.NodeId == target || cell.NodeId == target || paragraph.NodeIds.Contains(target, StringComparer.Ordinal));
            var spans = cell.NodeId == target && cell.Paragraphs.Count > 1 && change is not null
                ? mappedMultiParagraphCell ? ClipCellSpans(change.DifferenceSpans, offset, textLength, baseline) : [] : null;
            paragraphs.Add(paragraph with { Segments = SelectedSegments(paragraph.Segments, selected, change, baseline, spans) });
            offset += textLength + 1; // The parser joins cell paragraphs with a single newline.
        }
        return cell with { Paragraphs = paragraphs };
    }
    private static List<DifferenceSpan> ClipCellSpans(IReadOnlyList<DifferenceSpan> spans, int paragraphOffset, int length, bool baseline)
    {
        var clipped = new List<DifferenceSpan>();
        foreach (var span in spans)
        {
            var start = baseline ? span.BaselineStart : span.CurrentStart;
            var count = baseline ? span.BaselineLength : span.CurrentLength;
            var first = Math.Max(start, paragraphOffset); var last = Math.Min(start + count, paragraphOffset + length);
            if (first >= last) continue;
            clipped.Add(baseline ? span with { BaselineStart = first - paragraphOffset, BaselineLength = last - first }
                : span with { CurrentStart = first - paragraphOffset, CurrentLength = last - first });
        }
        return clipped;
    }
    private static IReadOnlyList<PreviewSegment> SelectedSegments(IReadOnlyList<PreviewSegment> segments, bool selected,
        ComparisonChangeItem? change, bool baseline, IReadOnlyList<DifferenceSpan>? spans = null)
    {
        if (!selected || change is null) return segments.Select(segment => segment with { Changed = false }).ToArray();
        var text = string.Concat(segments.Select(segment => segment.Text));
        var whole = change.Kind is ComparisonChangeKind.ParagraphInsert or ComparisonChangeKind.ParagraphDelete ||
            change.FormatDifference is not null && change.DifferenceSpans.Count == 0;
        var highlights = DifferenceHighlight.Build(text, spans ?? change.DifferenceSpans, baseline, whole);
        var selectedSegments = new List<PreviewSegment>();
        var sourceIndex = 0; var sourceOffset = 0; var highlightIndex = 0; var highlightOffset = 0;
        while (sourceIndex < segments.Count && highlightIndex < highlights.Count)
        {
            var source = segments[sourceIndex]; var highlight = highlights[highlightIndex];
            var length = Math.Min(source.Text.Length - sourceOffset, highlight.Text.Length - highlightOffset);
            if (length > 0) selectedSegments.Add(new(source.Text.Substring(sourceOffset, length), source.Format, highlight.Changed));
            sourceOffset += length; highlightOffset += length;
            if (sourceOffset >= source.Text.Length) { sourceIndex++; sourceOffset = 0; }
            if (highlightOffset >= highlight.Text.Length) { highlightIndex++; highlightOffset = 0; }
        }
        return selectedSegments;
    }
    /// <summary>Convenience for an explicit user viewport change; UI separates activation from delayed viewport events.</summary>
    public void ViewportMoved(bool baseline, int centerBlock)
    { var panel = baseline ? BaselinePanel : CurrentPanel; Scrolling.UserActivated(panel); Scrolling.ViewportChanged(panel, centerBlock); }
    partial void OnLinkedChanged(bool value) => Scrolling.SetLinked(value);
}
