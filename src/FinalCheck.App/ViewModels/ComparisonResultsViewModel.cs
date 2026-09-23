using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.App.ViewModels;

public sealed record DiffSegment(string Text, bool Changed);
public static class DifferenceHighlight
{
    public static IReadOnlyList<DiffSegment> Build(string text, IReadOnlyList<DifferenceSpan> spans, bool baseline, bool whole = false)
    {
        if (whole) return [new(text, true)];
        var ranges = spans.Select(s => (Start: baseline ? s.BaselineStart : s.CurrentStart, Length: baseline ? s.BaselineLength : s.CurrentLength))
            .Where(r => r.Length > 0 && r.Start >= 0 && r.Start <= text.Length && r.Length <= text.Length - r.Start).OrderBy(r => r.Start);
        var segments = new List<DiffSegment>(); var position = 0;
        foreach (var range in ranges)
        {
            var end = range.Start + range.Length;
            if (end <= position) continue;
            if (range.Start > position) segments.Add(new(text[position..range.Start], false));
            var start = Math.Max(position, range.Start); segments.Add(new(text[start..end], true)); position = end;
        }
        if (position < text.Length) segments.Add(new(text[position..], false));
        return segments;
    }
}

public sealed partial class ChangeItemViewModel : ViewModelBase
{
    public ComparisonChangeItem RawItem { get; }
    public ComparisonChangeItem Item { get; private set; }
    public bool IsHiddenByRules { get; private set; }
    public ChangeItemViewModel(ComparisonChangeItem item, DocumentSnapshot current, Action<ChangeItemViewModel> select,
        IReadOnlyDictionary<string, string>? baselineLocations = null, IReadOnlyDictionary<string, string>? currentLocations = null)
    {
        RawItem = Item = item; SelectCommand = new RelayCommand(() => select(this));
        Location = $"原位置：{LocationName(item.BaselineNodeId, baselineLocations)} → 当前位置：{LocationName(item.CurrentNodeId, currentLocations)}";
        CommentDetails = string.Join("\n", current.Comments.Where(c => item.CommentIds.Contains(c.CommentId, StringComparer.Ordinal)).Select(c => $"{c.Author ?? "未知作者"} · {c.TimestampUtc?.ToLocalTime():yyyy-MM-dd HH:mm}\n{c.Text}"));
        RevisionDetails = string.Join("\n", current.Revisions.Where(r => item.RevisionIds.Contains(r.RevisionId, StringComparer.Ordinal)).Select(r => $"Word {RevisionName(r.Kind)} · {r.Author ?? "未知作者"} · {r.TimestampUtc?.ToLocalTime():yyyy-MM-dd HH:mm}\n{r.Text}"));
    }
    public IRelayCommand SelectCommand { get; }
    public string ChangeId => Item.ChangeId;
    [ObservableProperty] private ComparisonReviewState reviewState;
    public string ReviewLabel => ReviewState switch { ComparisonReviewState.Confirmed => "已审阅", ComparisonReviewState.Ignored => "忽略", _ => "未处理" };
    partial void OnReviewStateChanged(ComparisonReviewState value) => OnPropertyChanged(nameof(ReviewLabel));
    public string TypeLabel => TypeName(Item.Kind);
    public string Location { get; }
    public string Summary => Item.DifferenceSpans.Count > 0 ? string.Join("；", Item.DifferenceSpans.Select(s => $"{Short(s.OldText)} → {Short(s.NewText)}")) :
        Item.FormatDifference is not null ? "格式发生变化，展开查看属性" : Item.Kind == ComparisonChangeKind.Comment ? "批注" : Short(Item.CurrentText.Length > 0 ? Item.CurrentText : Item.BaselineText);
    public string Hint => Short(Item.CurrentText.Length > 0 ? Item.CurrentText : Item.BaselineText);
    public bool LowConfidence => Item.MatchConfidence is ComparisonConfidenceLevel.Medium or ComparisonConfidenceLevel.Low;
    public string ConfidenceText => LowConfidence ? "匹配可信度较低，请人工确认。" : "";
    public IReadOnlyList<DiffSegment> BaselineSegments => DifferenceHighlight.Build(Item.BaselineText, Item.DifferenceSpans, true, Item.Kind == ComparisonChangeKind.ParagraphDelete);
    public IReadOnlyList<DiffSegment> CurrentSegments => DifferenceHighlight.Build(Item.CurrentText, Item.DifferenceSpans, false, Item.Kind == ComparisonChangeKind.ParagraphInsert);
    public IReadOnlyList<string> FormatDetails => Item.FormatDifference?.Properties.Select(p => $"{PropertyName(p.Property)}：{FormatValue(p.Property, p.BaselineValue)} → {FormatValue(p.Property, p.CurrentValue)}").ToArray() ?? [];
    public string CommentDetails { get; }
    public string RevisionDetails { get; }
    public string Diagnostics => string.Join("、", Item.DiagnosticCodes);
    public void ApplyRules(ComparisonIgnoreRules rules, bool showAll)
    {
        var projection = showAll ? RawItem : ComparisonIgnoreProjection.Project(RawItem, rules);
        IsHiddenByRules = projection is null;
        Item = projection ?? RawItem;
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(FormatDetails));
        OnPropertyChanged(nameof(BaselineSegments)); OnPropertyChanged(nameof(CurrentSegments));
    }
    private static string Short(string text) => text.Length == 0 ? "（无）" : text.Length > 70 ? text[..70] + "…" : text;
    private static string LocationName(string? id, IReadOnlyDictionary<string, string>? locations) => id is null ? "无对应位置" :
        locations is not null && locations.TryGetValue(id, out var value) ? value : "位置暂无法定位";
    public static IReadOnlyDictionary<string, string> Locations(DocumentSnapshot snapshot)
    {
        var locations = new Dictionary<string, string>(StringComparer.Ordinal);
        void AddParagraph(DocumentParagraphSnapshot paragraph, string label)
        { locations[paragraph.NodeId] = label; foreach (var run in paragraph.Runs) locations[run.NodeId] = label; }
        foreach (var paragraph in snapshot.Paragraphs) AddParagraph(paragraph, $"正文第 {paragraph.Index + 1} 段");
        foreach (var table in snapshot.Tables)
        {
            locations[table.NodeId] = $"表格 {table.Index + 1}";
            foreach (var row in table.Rows)
            {
                locations[row.NodeId] = $"表格 {table.Index + 1} / 第 {row.Index + 1} 行";
                foreach (var cell in row.Cells)
                {
                    var label = $"表格 {table.Index + 1} / 第 {cell.RowIndex + 1} 行第 {cell.ColumnIndex + 1} 列";
                    locations[cell.NodeId] = label;
                    foreach (var paragraph in cell.Paragraphs) AddParagraph(paragraph, label + $" / 第 {paragraph.Index + 1} 段");
                }
            }
        }
        return locations;
    }
    public static string TypeName(ComparisonChangeKind kind) => kind switch
    {
        ComparisonChangeKind.TextInsert => "文字新增", ComparisonChangeKind.TextDelete => "文字删除", ComparisonChangeKind.TextReplace => "文字修改",
        ComparisonChangeKind.ParagraphInsert => "段落新增", ComparisonChangeKind.ParagraphDelete => "段落删除", ComparisonChangeKind.ParagraphMove => "段落移动",
        ComparisonChangeKind.ParagraphMoveAndModify => "移动并修改", ComparisonChangeKind.CharacterFormatChange => "字符格式", ComparisonChangeKind.ParagraphFormatChange => "段落格式",
        ComparisonChangeKind.TableChange => "表格变化", ComparisonChangeKind.TableCellChange => "单元格变化", ComparisonChangeKind.Comment => "批注", _ => "Word 原生修订",
    };
    private static string RevisionName(DocumentRevisionKind kind) => kind switch { DocumentRevisionKind.Insert => "插入修订", DocumentRevisionKind.Delete => "删除修订", _ => "格式修订" };
    private static string PropertyName(string name) => name switch
    {
        "Font.Ascii" => "英文字体", "Font.HighAnsi" => "西文字体", "Font.EastAsia" => "中文字体", "Font.ComplexScript" => "复杂文字字体",
        "FontSizeHalfPoints" => "字号", "Color" => "颜色", "Bold" => "粗体", "Italic" => "斜体", "Underline" => "下划线", "Strike" => "删除线", "Highlight" => "高亮",
        "Alignment" => "对齐", "LeftIndent" => "左缩进", "RightIndent" => "右缩进", "FirstLineIndent" => "首行缩进", "HangingIndent" => "悬挂缩进",
        "SpacingBefore" => "段前", "SpacingAfter" => "段后", "LineSpacing" => "行距", "LineRule" => "行距规则", "Width" => "宽度", "ShadingFill" => "底色",
        "VerticalAlignment" => "垂直对齐", "GridSpan" => "跨列数", "VerticalMerge" => "纵向合并", "TableStructureChanged" => "表格结构", _ => "其他格式（" + name + "）",
    };
    private static string FormatValue(string property, string? value)
    {
        if (value is null or "<null>") return "未指定";
        if (property == "FontSizeHalfPoints" && int.TryParse(value, CultureInfo.InvariantCulture, out var half)) return (half / 2d).ToString(CultureInfo.InvariantCulture) + " 磅";
        return value switch { "True" => "是", "False" => "否", "left" => "左对齐", "right" => "右对齐", "center" => "居中", "both" => "两端对齐", "single" => "单线", "none" => "无", _ => value };
    }
}

public sealed record ChangeListEntry(string Id, string TypeLabel, string Summary, IReadOnlyList<ChangeItemViewModel> Members, IReadOnlyList<ChangeItemViewModel>? AllMembers = null)
{
    public string CountLabel => AllMembers is { } all && all.Count != Members.Count ? $"共 {all.Count} 处 · 当前显示 {Members.Count} 处" : $"共 {Members.Count} 处";
    public string Hint => Members[0].Hint;
    public string Location => Members[0].Location;
    public string ReviewLabel
    {
        get
        {
            var states = (AllMembers ?? Members).Select(m => m.ReviewState).Distinct().ToArray();
            return states.Length == 1 ? states[0] switch { ComparisonReviewState.Confirmed => "已审阅", ComparisonReviewState.Ignored => "忽略", _ => "未处理" } :
                states.Contains(ComparisonReviewState.Confirmed) ? "部分已审阅" : "部分忽略";
        }
    }
}

public sealed partial class ComparisonResultsViewModel : ViewModelBase
{
    private readonly IComparisonWorkflowService? workflow;
    private readonly Stack<ComparisonReviewEdit> undo = new();
    private List<ChangeItemViewModel> visibleChanges = [];
    private bool rebuildingEntries;
    public ComparisonWorkflowResult Outcome { get; private set; }
    public IReadOnlyList<ChangeItemViewModel> Changes { get; }
    public ObservableCollection<ChangeListEntry> Entries { get; private set; } = [];
    public ComparisonPreviewViewModel Preview { get; private set; }
    public static Task<ComparisonResultsViewModel> CreateAsync(ComparisonWorkflowResult outcome, IComparisonWorkflowService? workflow = null) =>
        Task.Run(() => new ComparisonResultsViewModel(outcome, workflow));
    [ObservableProperty] private bool grouped = true;
    [ObservableProperty] private bool fullDocumentMode;
    public bool ContextMode => !FullDocumentMode;
    partial void OnFullDocumentModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ContextMode));
        if (value) Preview.Locate(SelectedChange?.Item);
    }
    [RelayCommand] private void ShowFullDocument() => FullDocumentMode = true;
    [RelayCommand] private void ShowContext() => FullDocumentMode = false;
    [ObservableProperty] private ChangeListEntry? selectedEntry;
    [ObservableProperty] private ChangeItemViewModel? selectedChange;
    [ObservableProperty] private string statusFilter = "全部";
    [ObservableProperty] private string typeFilter = "全部类型";
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private bool isSaving;
    [ObservableProperty] private string reviewMessage = "";
    [ObservableProperty] private bool showAllDifferences;
    partial void OnShowAllDifferencesChanged(bool value)
    {
        var linked = Preview.Linked;
        Preview = BuildPreview(value); Preview.Linked = linked;
        OnPropertyChanged(nameof(Preview));
        RefreshEntries(); Preview.Locate(SelectedChange?.Item);
    }
    public string IgnoreRulesLabel => Outcome.Record.IgnoreRules.IsEmpty ? "本次未启用比对忽略规则。" :
        $"本次已启用比对忽略规则；完整事实保留。内容：{(Outcome.Record.IgnoreRules.Punctuation ? "标点 " : "")}{(Outcome.Record.IgnoreRules.PageNumbers ? "页码 " : "")}{(Outcome.Record.IgnoreRules.Numbering ? "序号 " : "")}{(Outcome.Record.IgnoreRules.Characters.Length > 0 ? "自定义字符 " : "")}；格式：{(Outcome.Record.IgnoreRules.AllFormatting ? "全部" : string.Join("、", Outcome.Record.IgnoreRules.HiddenProperties))}";
    public IReadOnlyList<string> StatusOptions { get; } = ["全部", "未处理", "已审阅", "已忽略"];
    public IReadOnlyList<string> TypeOptions { get; } = ["全部类型", "文字", "格式", "新增", "删除", "移动", "表格", "批注", "修订"];
    public bool CanReview => workflow is not null && SelectedChange is not null && !IsSaving;
    public bool CanUndo => workflow is not null && undo.Count > 0 && !IsSaving;
    public bool CanPrevious => SelectedChange is not null && visibleChanges.IndexOf(SelectedChange) > 0;
    public bool CanNext => SelectedChange is not null && visibleChanges.IndexOf(SelectedChange) is var index && index >= 0 && index + 1 < visibleChanges.Count;
    public string ReviewStatistics => $"总变化 {Changes.Count} · 已审阅 {Changes.Count(c => c.ReviewState == ComparisonReviewState.Confirmed)} · 未处理 {Changes.Count(c => c.ReviewState == ComparisonReviewState.Unresolved)} · 忽略 {Changes.Count(c => c.ReviewState == ComparisonReviewState.Ignored)}";
    public int VisibleCount { get; private set; }
    public string CountLabel => $"{VisibleCount} / {Changes.Count} 项";
    public bool NoVisibleEntries => Entries.Count == 0;
    public ComparisonResultsViewModel(ComparisonWorkflowResult outcome, IComparisonWorkflowService? workflow = null)
    {
        Outcome = outcome; this.workflow = workflow;
        var baselineLocations = ChangeItemViewModel.Locations(outcome.Baseline); var currentLocations = ChangeItemViewModel.Locations(outcome.Current);
        Changes = outcome.Result.Changes.Select(item => new ChangeItemViewModel(item, outcome.Current, Select, baselineLocations, currentLocations)
            { ReviewState = outcome.Record.ReviewStates.GetValueOrDefault(item.ChangeId) }).ToArray();
        Preview = BuildPreview(false); RefreshEntries();
    }
    private ComparisonPreviewViewModel BuildPreview(bool showAll)
    {
        var projected = showAll ? Outcome.Result.Changes : Outcome.Result.Changes
            .Select(item => ComparisonIgnoreProjection.Project(item, Outcome.Record.IgnoreRules))
            .OfType<ComparisonChangeItem>().ToArray();
        return new(Outcome with { Result = Outcome.Result with { Changes = projected } });
    }
    public string Heading => $"{Outcome.Record.BaselineFile.Name} → {Outcome.Record.CurrentFile.Name}";
    public string ComparedAt => Outcome.Record.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    public string Statistics => $"共 {Outcome.Result.Statistics.TotalChanges} 项 · 文字 {Outcome.Result.Statistics.TextChanges} · 格式 {Outcome.Result.Statistics.FormatChanges} · 新增 {Outcome.Result.Statistics.ParagraphsAdded} · 删除 {Outcome.Result.Statistics.ParagraphsDeleted} · 移动 {Outcome.Result.Statistics.ParagraphsMoved} · 批注 {Outcome.Result.Statistics.Comments}";
    public string IntegrityNotice => Outcome.IsPartial ? "本次比对结果可能不完整。" : Outcome.Result.NodeMappings.Any(m => m.Confidence is ComparisonConfidenceLevel.Medium or ComparisonConfidenceLevel.Low) ? "部分匹配需人工确认。" : "";
    public string DiagnosticDetails => string.Join("\n", Outcome.Baseline.ParseDiagnostics.Concat(Outcome.Current.ParseDiagnostics).Select(d => $"{d.Code} · {d.NodeId ?? d.SourcePart}")
        .Concat(Outcome.Result.Diagnostics.Select(d => $"{d.Code} · {d.BaselineNodeId} → {d.CurrentNodeId}")));
    public bool HasChanges => Changes.Count > 0;
    public string EmptyMessage => !HasChanges ? "两份文档没有检测到修改。" : NoVisibleEntries ? "当前筛选没有结果，请调整搜索或筛选条件。" : SelectedChange is null ? "请选择左侧修改，查看原文、现文与证据。" : string.Empty;
    partial void OnGroupedChanged(bool value) => RefreshEntries();
    partial void OnSelectedEntryChanged(ChangeListEntry? value)
    {
        if (!rebuildingEntries) SelectedChange = value is { Members.Count: > 0 } ? value.Members[0] : null;
    }
    partial void OnSelectedChangeChanged(ChangeItemViewModel? value) { RefreshReviewCommands(); RefreshNavigationCommands(); OnPropertyChanged(nameof(EmptyMessage)); Preview.Locate(value?.Item); }
    partial void OnStatusFilterChanged(string value) => RefreshEntries();
    partial void OnTypeFilterChanged(string value) => RefreshEntries();
    partial void OnSearchTextChanged(string value) => RefreshEntries();
    partial void OnIsSavingChanged(bool value) => RefreshReviewCommands();
    private void RefreshReviewCommands()
    { OnPropertyChanged(nameof(CanReview)); OnPropertyChanged(nameof(CanUndo)); ReviewSelectedCommand.NotifyCanExecuteChanged(); ReviewGroupCommand.NotifyCanExecuteChanged(); UndoReviewCommand.NotifyCanExecuteChanged(); }
    private void RefreshNavigationCommands()
    { OnPropertyChanged(nameof(CanPrevious)); OnPropertyChanged(nameof(CanNext)); PreviousChangeCommand.NotifyCanExecuteChanged(); NextChangeCommand.NotifyCanExecuteChanged(); }
    [RelayCommand(CanExecute = nameof(CanPrevious))] private void PreviousChange() => Select(visibleChanges[visibleChanges.IndexOf(SelectedChange!) - 1]);
    [RelayCommand(CanExecute = nameof(CanNext))] private void NextChange() => Select(visibleChanges[visibleChanges.IndexOf(SelectedChange!) + 1]);
    [RelayCommand] private void OnlyUnresolved() => StatusFilter = "未处理";
    [RelayCommand(CanExecute = nameof(CanReview))] private Task ReviewSelectedAsync(string state) => UpdateReviewAsync([SelectedChange!.ChangeId], state);
    [RelayCommand(CanExecute = nameof(CanReview))] private Task ReviewGroupAsync(string state) => UpdateReviewAsync(SelectedEntry?.Members.Select(m => m.ChangeId).ToArray() ?? [], state);
    private async Task UpdateReviewAsync(string[] ids, string state)
    {
        if (!CanReview || ids.Length == 0 || !Enum.TryParse<ComparisonReviewState>(state, out var target) || !Enum.IsDefined(target)) return;
        var previous = ids.ToDictionary(id => id, id => Outcome.Record.ReviewStates[id], StringComparer.Ordinal);
        var targets = ids.ToDictionary(id => id, _ => target, StringComparer.Ordinal);
        if (ids.All(id => previous[id] == target)) return;
        IsSaving = true; ReviewMessage = "正在保存审阅状态…";
        try
        {
            var record = await workflow!.EditReviewAsync(Outcome.Record.RecordId, new(targets, previous));
            undo.Push(new(previous, targets)); ApplyRecord(record);
            ReviewMessage = target switch { ComparisonReviewState.Confirmed => "已标记为已审阅（仅表示人工查看）。", ComparisonReviewState.Ignored => "已忽略当前修改。", _ => "已恢复未处理。" };
        }
        catch (Exception) { ReviewMessage = "审阅状态未保存，未覆盖其他审阅。请重新打开历史后重试。"; }
        finally { IsSaving = false; }
    }
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoReviewAsync()
    {
        IsSaving = true; ReviewMessage = "正在撤销…";
        try
        {
            var record = await workflow!.EditReviewAsync(Outcome.Record.RecordId, undo.Peek());
            undo.Pop(); ApplyRecord(record); ReviewMessage = "已撤销，原审阅状态已保存。";
        }
        catch (Exception) { ReviewMessage = "无法撤销：保存失败或状态已被其他操作更改。未覆盖较新的审阅，请重新打开历史确认。"; }
        finally { IsSaving = false; }
    }
    private void ApplyRecord(ComparisonRecord record)
    {
        Outcome = Outcome with { Record = record };
        foreach (var change in Changes) change.ReviewState = record.ReviewStates.GetValueOrDefault(change.ChangeId);
        RefreshEntries(); OnPropertyChanged(nameof(ReviewStatistics));
    }
    private void Select(ChangeItemViewModel item)
    {
        var entry = Entries.FirstOrDefault(e => e.Members.Contains(item));
        if (entry is null) return;
        SelectedEntry = entry; SelectedChange = item;
    }
    private void RefreshEntries()
    {
        foreach (var change in Changes) change.ApplyRules(Outcome.Record.IgnoreRules, ShowAllDifferences);
        var previousId = SelectedEntry?.Id; var changeId = SelectedChange?.ChangeId; var entries = new List<ChangeListEntry>();
        var visible = Changes.Where(Matches).ToArray(); VisibleCount = visible.Length;
        var byId = visible.ToDictionary(c => c.ChangeId, StringComparer.Ordinal); var all = Changes.ToDictionary(c => c.ChangeId, StringComparer.Ordinal);
        var included = new HashSet<string>(StringComparer.Ordinal);
        if (Grouped)
        {
            foreach (var group in Outcome.Result.Groups)
            {
                var members = group.ChangeIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray(); if (members.Length == 0) continue;
                foreach (var member in members) included.Add(member.ChangeId);
                entries.Add(new(group.GroupId, ChangeItemViewModel.TypeName(group.Kind), string.Join("；", members.Take(2).Select(member => member.Summary)), members, group.ChangeIds.Where(all.ContainsKey).Select(id => all[id]).ToArray()));
            }
        }
        foreach (var item in visible.Where(item => !included.Contains(item.ChangeId))) entries.Add(new(item.ChangeId, item.TypeLabel, item.Summary, [item]));
        visibleChanges = entries.SelectMany(entry => entry.Members).Distinct().ToList();
        Entries = new(entries); OnPropertyChanged(nameof(Entries));
        rebuildingEntries = true;
        SelectedEntry = Entries.FirstOrDefault(entry => entry.Members.Any(c => c.ChangeId == changeId)) ?? Entries.FirstOrDefault(entry => entry.Id == previousId) ?? (Entries.Count > 0 ? Entries[0] : null);
        rebuildingEntries = false;
        SelectedChange = SelectedEntry?.Members.FirstOrDefault(c => c.ChangeId == changeId) ?? (SelectedEntry is { Members.Count: > 0 } currentEntry ? currentEntry.Members[0] : null);
        RefreshNavigationCommands();
        OnPropertyChanged(nameof(CountLabel)); OnPropertyChanged(nameof(NoVisibleEntries)); OnPropertyChanged(nameof(EmptyMessage));
    }
    private bool Matches(ChangeItemViewModel item)
    {
        if (item.IsHiddenByRules) return false;
        if (!(StatusFilter switch { "未处理" => item.ReviewState == ComparisonReviewState.Unresolved, "已审阅" => item.ReviewState == ComparisonReviewState.Confirmed,
            "已忽略" => item.ReviewState == ComparisonReviewState.Ignored, _ => item.ReviewState != ComparisonReviewState.Ignored })) return false;
        var kind = item.Item.Kind;
        if (!(TypeFilter switch
        {
            "文字" => item.Item.DifferenceSpans.Count > 0 || kind is ComparisonChangeKind.ParagraphInsert or ComparisonChangeKind.ParagraphDelete, "格式" => item.Item.FormatDifference is not null,
            "新增" => kind is ComparisonChangeKind.TextInsert or ComparisonChangeKind.ParagraphInsert,
            "删除" => kind is ComparisonChangeKind.TextDelete or ComparisonChangeKind.ParagraphDelete,
            "移动" => kind is ComparisonChangeKind.ParagraphMove or ComparisonChangeKind.ParagraphMoveAndModify,
            "表格" => kind is ComparisonChangeKind.TableChange or ComparisonChangeKind.TableCellChange,
            "批注" => item.Item.CommentIds.Count > 0 || kind == ComparisonChangeKind.Comment,
            "修订" => item.Item.RevisionIds.Count > 0 || kind == ComparisonChangeKind.NativeRevision, _ => true,
        })) return false;
        var query = SearchText.Trim();
        return query.Length == 0 || item.Item.BaselineText.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Item.CurrentText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.Hint.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Location.Contains(query, StringComparison.OrdinalIgnoreCase) || item.CommentDetails.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
