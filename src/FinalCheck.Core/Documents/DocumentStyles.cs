namespace FinalCheck.Core.Documents;

public enum DocumentStyleKind
{
    Paragraph,
    Character,
    Table,
    Numbering,
    Unknown,
}

public sealed record DocumentDefaultsSnapshot(
    CharacterFormatSnapshot CharacterFormatting,
    ParagraphFormatSnapshot ParagraphFormatting)
{
    public static DocumentDefaultsSnapshot Empty { get; } = new(CharacterFormatSnapshot.Empty, ParagraphFormatSnapshot.Empty);
}

public sealed record DocumentStyleSnapshot(
    string StyleId,
    string? Name,
    DocumentStyleKind Kind,
    string? BasedOnStyleId,
    string? LinkedStyleId,
    bool IsDefault,
    CharacterFormatSnapshot CharacterFormatting,
    ParagraphFormatSnapshot ParagraphFormatting,
    TableFormatSnapshot TableFormatting);

public sealed record ParagraphNumberingReferenceSnapshot(
    int NumberingId,
    int LevelIndex,
    int? AbstractNumberingId,
    string? NumberFormat,
    string? LevelText,
    int? StartValue);

public sealed record NumberingLevelSnapshot(int LevelIndex, string? NumberFormat, string? LevelText, int? StartValue);

public sealed record AbstractNumberingSnapshot(int AbstractNumberingId, IReadOnlyList<NumberingLevelSnapshot> Levels);

public sealed record NumberingInstanceSnapshot(int NumberingId, int AbstractNumberingId);

public sealed record DocumentNumberingSnapshot(
    IReadOnlyList<AbstractNumberingSnapshot> AbstractDefinitions,
    IReadOnlyList<NumberingInstanceSnapshot> Instances)
{
    public static DocumentNumberingSnapshot Empty { get; } = new([], []);
}
