using System.Text.Json.Serialization;

namespace FinalCheck.Core.Documents;

public sealed record DocumentSectionSnapshot(
    DocumentNodeIdentitySnapshot Identity,
    int Index,
    uint? PageWidthTwips,
    uint? PageHeightTwips,
    string? Orientation,
    int? MarginTopTwips,
    uint? MarginRightTwips,
    int? MarginBottomTwips,
    uint? MarginLeftTwips);

public sealed record DocumentTableSnapshot(
    DocumentNodeIdentitySnapshot Identity,
    int Index,
    IReadOnlyList<DocumentTableRowSnapshot> Rows,
    TableFormatSnapshot DirectFormatting)
{
    [JsonIgnore]
    public string NodeId => Identity.NodeId;
}

public sealed record DocumentTableRowSnapshot(
    DocumentNodeIdentitySnapshot Identity,
    int Index,
    IReadOnlyList<DocumentTableCellSnapshot> Cells,
    uint? HeightTwips,
    string? HeightRule)
{
    [JsonIgnore]
    public string NodeId => Identity.NodeId;
}

public sealed record DocumentTableCellSnapshot(
    DocumentNodeIdentitySnapshot Identity,
    int Index,
    int RowIndex,
    int ColumnIndex,
    string DisplayText,
    IReadOnlyList<DocumentParagraphSnapshot> Paragraphs,
    TableCellFormatSnapshot DirectFormatting,
    int NestedTableCount)
{
    [JsonIgnore]
    public string NodeId => Identity.NodeId;

    [JsonIgnore]
    public string Text => DisplayText;
}

public sealed record HeaderFooterSnapshot(
    string Kind,
    string RelationshipId,
    string SourcePart,
    string Text);

public sealed record DocumentHyperlinkSnapshot(
    string NodeId,
    string ParagraphNodeId,
    string DisplayText,
    string? RelationshipId,
    string? Target,
    bool IsExternal);

public sealed record DocumentProtectionSnapshot(
    bool IsPresent,
    bool Enforcement,
    string? EditMode,
    string? AlgorithmName,
    string? AlgorithmSid,
    string? ProviderType,
    string? SpinCount,
    string? Hash,
    string? Salt)
{
    public static DocumentProtectionSnapshot None { get; } = new(false, false, null, null, null, null, null, null, null);
}
