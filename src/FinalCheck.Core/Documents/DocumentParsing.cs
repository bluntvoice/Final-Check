namespace FinalCheck.Core.Documents;

public enum DocumentParseStage
{
    Opening,
    ReadingStyles,
    ReadingNumbering,
    ReadingDocument,
    ReadingTables,
    ReadingComments,
    BuildingSnapshot,
    Completed,
}

public sealed record DocumentParseProgress(DocumentParseStage Stage, int ProcessedItems, int? TotalItems);

public enum DocumentParseErrorKind
{
    FileNotFound,
    UnsupportedFormat,
    Encrypted,
    InvalidPackage,
    CorruptedPackage,
}

public sealed class DocumentParseException : Exception
{
    public DocumentParseException(DocumentParseErrorKind errorKind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorKind = errorKind;
    }

    public DocumentParseErrorKind ErrorKind { get; }
}
