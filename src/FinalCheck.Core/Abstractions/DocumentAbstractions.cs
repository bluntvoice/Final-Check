using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;

namespace FinalCheck.Core.Abstractions;

public interface IDocumentParser
{
    ValueTask<DocumentSnapshot> ParseFileAsync(
        string filePath,
        IProgress<DocumentParseProgress>? progress = null,
        CancellationToken cancellationToken = default);

    ValueTask<DocumentSnapshot> ParseAsync(
        Stream docxStream,
        IProgress<DocumentParseProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IDocumentSnapshotSerializer
{
    byte[] Serialize(DocumentSnapshot snapshot);

    DocumentSnapshot Deserialize(ReadOnlySpan<byte> payload);
}

public interface IComparisonResultSerializer
{
    byte[] Serialize(ComparisonResult result);

    ComparisonResult Deserialize(ReadOnlySpan<byte> payload);
}

public interface IDocumentFormatService
{
    ValueTask ApplyFormatAsync(
        Stream sourceDocx,
        Stream destinationDocx,
        IReadOnlyList<DocumentNodeMapping> mappings,
        CancellationToken cancellationToken = default);
}

public interface IDocumentPreviewRenderer
{
    ValueTask<string> RenderAsync(
        DocumentSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
