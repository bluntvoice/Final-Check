using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.AcceptanceFixtures;

namespace FinalCheck.Documents.Tests;

public sealed class AcceptanceFixtureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FinalCheck-Acceptance-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CatalogProducesValidFilesAndReadOnlyVerification()
    {
        var manifest = AcceptanceFixtureCatalog.Generate(root);
        Assert.True(manifest.SyntheticOnly);
        Assert.Equal(18, manifest.Files.Count);
        Assert.Equal(400, manifest.Files.Single(file => file.Name == "long-baseline.docx").Paragraphs);
        Assert.Equal(508, manifest.Files.Single(file => file.Name == "long-current.docx").Paragraphs);
        Assert.Equal(1, manifest.Files.Single(file => file.Name == "small-current.docx").Tables);
        Assert.Equal(1, manifest.Files.Single(file => file.Name == "small-current.docx").Comments);
        var before = Directory.GetFiles(root).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        Assert.Equal(manifest.Files, AcceptanceFixtureCatalog.Verify(root).Files);
        foreach (var (name, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, name!)));
    }

    [Fact]
    public void GenerationRefusesExistingDirectoryAndPreservesFiles()
    {
        Directory.CreateDirectory(root);
        var marker = Path.Combine(root, "keep.txt");
        File.WriteAllText(marker, "keep");
        Assert.Throws<IOException>(() => AcceptanceFixtureCatalog.Generate(root));
        Assert.Equal("keep", File.ReadAllText(marker));
        Assert.Single(Directory.GetFiles(root));
    }

    [Fact]
    public void VerificationRejectsTamperedFile()
    {
        AcceptanceFixtureCatalog.Generate(root);
        File.AppendAllText(Path.Combine(root, "page-current.docx"), "tamper");
        Assert.Throws<InvalidDataException>(() => AcceptanceFixtureCatalog.Verify(root));
    }

    [Fact]
    public void VerificationRejectsTraversalBeforeReadingOutsideCatalog()
    {
        var manifest = AcceptanceFixtureCatalog.Generate(root);
        var files = manifest.Files.ToArray();
        files[0] = files[0] with { Name = "../outside.docx" };
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest with { Files = files }));
        Assert.Throws<InvalidDataException>(() => AcceptanceFixtureCatalog.Verify(root));
    }

    [Fact]
    public void RelativeOutputIsRejected()
    {
        Assert.Throws<ArgumentException>(() => AcceptanceFixtureCatalog.Generate("relative-fixtures"));
    }

    [Fact]
    public void RegenerationOnAnotherDirectoryHasIdenticalDocumentXml()
    {
        var first = AcceptanceFixtureCatalog.Generate(root);
        var otherRoot = Path.Combine(root, "regenerated");
        var second = AcceptanceFixtureCatalog.Generate(otherRoot);
        Assert.Equal(first.Files.Select(file => file.Name), second.Files.Select(file => file.Name));
        foreach (var file in first.Files)
        {
            using var original = WordprocessingDocument.Open(Path.Combine(root, file.Name), false);
            using var repeated = WordprocessingDocument.Open(Path.Combine(otherRoot, file.Name), false);
            Assert.Equal(original.MainDocumentPart!.Document!.OuterXml, repeated.MainDocumentPart!.Document!.OuterXml);
        }
    }

    [Fact]
    public void GeneratedSamplesRetainPageAndSubstantiveNumberChanges()
    {
        AcceptanceFixtureCatalog.Generate(root);
        using var baseline = WordprocessingDocument.Open(Path.Combine(root, "page-baseline.docx"), false);
        using var current = WordprocessingDocument.Open(Path.Combine(root, "page-current.docx"), false);
        Assert.Contains("Page 100", baseline.MainDocumentPart!.Document!.InnerText);
        Assert.Contains("Page 101", current.MainDocumentPart!.Document!.InnerText);
        Assert.Contains("300万元，付款期限30日，签订于2026年9月", baseline.MainDocumentPart!.Document!.InnerText);
        Assert.Contains("400万元，付款期限60日，签订于2027年9月", current.MainDocumentPart!.Document!.InnerText);
        using var move = WordprocessingDocument.Open(Path.Combine(root, "move-current.docx"), false);
        Assert.Contains("第二段 付款期限为60日", move.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ElementAt(3).InnerText);
    }

    [Fact]
    public async Task AllGeneratedSamplesParseWithTheRealDocumentEngine()
    {
        var manifest = AcceptanceFixtureCatalog.Generate(root);
        var parser = new OpenXmlDocumentParser();
        foreach (var file in manifest.Files)
        {
            await using var stream = File.OpenRead(Path.Combine(root, file.Name));
            var snapshot = await parser.ParseAsync(stream);
            Assert.Equal(file.Paragraphs, snapshot.Paragraphs.Count);
            Assert.Equal(file.Tables, snapshot.Tables.Count);
            Assert.Equal(file.Comments, snapshot.Comments.Count);
            Assert.Equal(FinalCheck.Core.Documents.DocumentParseStatus.Complete, snapshot.ParseStatus);
            Assert.Empty(snapshot.ParseDiagnostics);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        GC.SuppressFinalize(this);
    }
}
