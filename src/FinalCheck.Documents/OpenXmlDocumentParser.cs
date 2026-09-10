using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Documents;

namespace FinalCheck.Documents;

public sealed class OpenXmlDocumentParser : IDocumentParser
{
    private static readonly byte[] OleCompoundSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public async ValueTask<DocumentSnapshot> ParseFileAsync(
        string filePath,
        IProgress<DocumentParseProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new DocumentParseException(DocumentParseErrorKind.FileNotFound, $"The DOCX file was not found: {filePath}");
        }

        if (!string.Equals(Path.GetExtension(filePath), ".docx", StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentParseException(DocumentParseErrorKind.UnsupportedFormat, "Only .docx files are supported.");
        }

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ParseAsync(stream, progress, cancellationToken);
    }

    public ValueTask<DocumentSnapshot> ParseAsync(
        Stream docxStream,
        IProgress<DocumentParseProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(docxStream);
        if (!docxStream.CanRead || !docxStream.CanSeek)
        {
            throw new ArgumentException("The DOCX stream must be readable and seekable.", nameof(docxStream));
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new DocumentParseProgress(DocumentParseStage.Opening, 0, null));
        ValidatePackageSignature(docxStream);
        docxStream.Position = 0;
        var sha256 = Convert.ToHexString(SHA256.HashData(docxStream)).ToLowerInvariant();
        docxStream.Position = 0;

        try
        {
            using var document = WordprocessingDocument.Open(docxStream, false);
            var context = new OpenXmlParserContext(document, sha256, cancellationToken, progress);
            return ValueTask.FromResult(context.Parse());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DocumentParseException)
        {
            throw;
        }
        catch (Exception exception) when (IsPackageFailure(exception))
        {
            throw new DocumentParseException(
                DocumentParseErrorKind.CorruptedPackage,
                "The DOCX package is invalid or corrupted and could not be parsed.",
                exception);
        }
    }

    private static void ValidatePackageSignature(Stream stream)
    {
        var originalPosition = stream.Position;
        Span<byte> signature = stackalloc byte[8];
        stream.Position = 0;
        var bytesRead = stream.Read(signature);
        stream.Position = originalPosition;

        if (bytesRead >= OleCompoundSignature.Length && signature.SequenceEqual(OleCompoundSignature))
        {
            throw new DocumentParseException(
                DocumentParseErrorKind.Encrypted,
                "The file uses an OLE compound envelope and may be an encrypted Office document.");
        }

        var isZip = bytesRead >= 4 && signature[0] == 0x50 && signature[1] == 0x4B &&
            ((signature[2] == 0x03 && signature[3] == 0x04) ||
             (signature[2] == 0x05 && signature[3] == 0x06) ||
             (signature[2] == 0x07 && signature[3] == 0x08));
        if (!isZip)
        {
            throw new DocumentParseException(
                DocumentParseErrorKind.UnsupportedFormat,
                "The input is not an Open XML ZIP package.");
        }
    }

    private static bool IsPackageFailure(Exception exception) =>
        exception is OpenXmlPackageException or InvalidDataException or IOException ||
        string.Equals(exception.GetType().Name, "FileFormatException", StringComparison.Ordinal);
}
