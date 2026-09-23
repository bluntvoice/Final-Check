namespace FinalCheck.App.ViewModels;

public readonly record struct DocumentPanelId(string Value);
public sealed record WorkspaceDocumentPanel(DocumentPanelId Id, string Title, IReadOnlyList<PreviewBlock> Blocks);
public sealed record LogicalNodeLink(DocumentPanelId Source, string SourceNodeId, DocumentPanelId Target, string TargetNodeId);
public sealed record LogicalScrollRequest(DocumentPanelId Panel, int BlockIndex);

/// <summary>Panel-count independent logical anchoring. Only explicit user input may promote a follower.</summary>
public sealed class LogicalScrollCoordinator
{
    private readonly Dictionary<DocumentPanelId, WorkspaceDocumentPanel> panels;
    private readonly Dictionary<(DocumentPanelId, string), List<(DocumentPanelId, int)>> links = [];
    private readonly Dictionary<DocumentPanelId, int> anchors = [];
    private (DocumentPanelId Panel, int Index)? lastAligned;
    public DocumentPanelId? Leader { get; private set; }
    public bool Linked { get; private set; } = true;
    public event Action<LogicalScrollRequest>? ScrollRequested;
    public event Action? Unmatched;
    public LogicalScrollCoordinator(IReadOnlyList<WorkspaceDocumentPanel> panels, IReadOnlyList<LogicalNodeLink> mappings)
    {
        this.panels = panels.ToDictionary(p => p.Id);
        var indexes = panels.ToDictionary(p => p.Id, p => p.Blocks.SelectMany((block, index) => block.NodeIds.Select(id => (id, index)))
            .GroupBy(pair => pair.id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().index, StringComparer.Ordinal));
        foreach (var group in mappings.GroupBy(m => (m.Source, m.SourceNodeId, m.Target)))
        {
            var targets = group.Select(m => m.TargetNodeId).Distinct(StringComparer.Ordinal).ToArray();
            if (targets.Length != 1 || !indexes.TryGetValue(group.Key.Target, out var index) || !index.TryGetValue(targets[0], out var block)) continue;
            var key = (group.Key.Source, group.Key.SourceNodeId);
            if (!links.TryGetValue(key, out var values)) links[key] = values = [];
            values.Add((group.Key.Target, block));
        }
    }
    public void UserActivated(DocumentPanelId panel)
    {
        if (!panels.ContainsKey(panel)) return;
        if (Leader != panel) lastAligned = null;
        Leader = panel;
    }
    public void ViewportChanged(DocumentPanelId panel, int centerBlock)
    {
        if (Leader != panel || !panels.TryGetValue(panel, out var source) || centerBlock < 0 || centerBlock >= source.Blocks.Count) return;
        anchors[panel] = centerBlock;
        if (Linked) Align(panel, centerBlock);
    }
    public void SetLinked(bool linked)
    {
        if (Linked == linked) return;
        Linked = linked; lastAligned = null;
        if (linked && Leader is { } panel && anchors.TryGetValue(panel, out var anchor)) Align(panel, anchor);
    }
    public void ResetNavigation() { Leader = null; lastAligned = null; }
    private void Align(DocumentPanelId panel, int anchor)
    {
        if (lastAligned == (panel, anchor)) return;
        lastAligned = (panel, anchor);
        var blocks = panels[panel].Blocks; var aligned = new HashSet<DocumentPanelId>();
        for (var distance = 0; distance <= 2; distance++)
        {
            foreach (var position in new[] { anchor + distance, anchor - distance }.Distinct())
            {
                if (position < 0 || position >= blocks.Count) continue;
                foreach (var id in blocks[position].NodeIds)
                    if (links.TryGetValue((panel, id), out var targets))
                        foreach (var (target, index) in targets)
                            if (target != panel && aligned.Add(target)) ScrollRequested?.Invoke(new(target, index));
            }
        }
        if (aligned.Count == 0) Unmatched?.Invoke();
    }
}
