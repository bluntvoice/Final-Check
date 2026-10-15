using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace FinalCheck.AcceptanceFixtures;

public sealed record FixtureFile(string Name, long Bytes, string Sha256, int Paragraphs, int Tables, int Comments);
public sealed record FixtureManifest(int SchemaVersion, bool SyntheticOnly, IReadOnlyList<FixtureFile> Files);

/// <summary>Purely synthetic files; no database, bootstrap, installed app or external DOCX input.</summary>
public static class AcceptanceFixtureCatalog
{
    private const string ManifestName = "manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] Names =
    [
        "small-baseline.docx", "small-current.docx", "medium-baseline.docx", "medium-current.docx",
        "long-baseline.docx", "long-current.docx",
        "00-template-v1.docx", "01-own-v1.docx", "02-counterparty-v1.docx", "03-own-v2.docx",
        "04-counterparty-v2.docx", "05-template-v2.docx",
        "ignore-baseline.docx", "ignore-current.docx", "move-baseline.docx", "move-current.docx",
        "page-baseline.docx", "page-current.docx"
    ];

    public static FixtureManifest Generate(string directory)
    {
        var root = ValidateRoot(directory);
        if (Directory.Exists(root) || File.Exists(root))
            throw new IOException("Output must be a new directory; existing content is never overwritten.");
        Directory.CreateDirectory(root);
        foreach (var count in new[] { 20, 400 })
        foreach (var baseline in new[] { true, false })
        {
            var prefix = count == 20 ? "small" : "medium";
            WriteDocument(root, $"{prefix}-{Side(baseline)}.docx", main =>
            {
                for (var index = 0; index < count; index++)
                {
                    main.Document!.Body!.Append(FormattedParagraph(index, baseline));
                    if (index == 3) main.Document!.Body!.Append(AmountTable(baseline));
                }
                AddComment(main);
            });
        }

        foreach (var baseline in new[] { true, false })
            WriteDocument(root, $"long-{Side(baseline)}.docx", main =>
            {
                for (var index = 0; index < 400; index++)
                {
                    if (!baseline && index is >= 100 and < 120) continue;
                    main.Document!.Body!.Append(FormattedParagraph(index, baseline && index % 7 == 0));
                    if (!baseline && index == 200)
                        for (var added = 0; added < 128; added++)
                            main.Document!.Body!.Append(TextParagraph($"新增模拟条款{added + 1}：验收材料由虚构双方核对，不涉及客户。"));
                }
                main.Document!.Body!.Append(AmountTable(baseline));
                AddComment(main);
            });

        foreach (var (name, days) in new[]
        {
            ("00-template-v1", 30), ("01-own-v1", 30), ("02-counterparty-v1", 60),
            ("03-own-v2", 45), ("04-counterparty-v2", 90), ("05-template-v2", 35)
        })
            WriteDocument(root, name + ".docx", main =>
            {
                main.Document!.Body!.Append(TextParagraph("纯合成合同版本链 · 人工验收样本"));
                for (var index = 0; index < 12; index++)
                    main.Document!.Body!.Append(TextParagraph($"第{index + 1}条 模拟甲乙双方付款期限为{days}日，全部数据虚构。"));
            });

        WritePair(root, "ignore",
            ["合同履行与结算规则如下。", "甲方应履行付款义务，乙方应提供收款确认。", "第 3 页", "1. 甲乙双方约定付款期限为30日，并于收到发票后结算。", "甲方应当※及时付款并出具回执。", "合同总金额300万元，含税并按约定支付。", "合同签订日期为2026年9月，双方以盖章时间为准。", "本合同一式两份，双方各执一份。"],
            ["合同履行与结算规则如下。", "甲方应履行付款义务；乙方应提供收款确认。", "第 4 页", "2. 甲乙双方约定付款期限为60日，并于收到发票后结算。", "甲方应当及时付款并出具回执。", "合同总金额400万元，含税并按约定支付。", "合同签订日期为2027年9月，双方以盖章时间为准。", "本合同一式两份，双方各执一份。"]);
        WritePair(root, "move",
            ["第一段 合同定义与适用范围。", "第二段 付款期限为30日，双方应核实发票与金额。", "第三段 保密义务在合同终止后继续有效。", "第四段 争议提交约定法院解决。", "第五段 双方按照约定履行交付义务。"],
            ["第一段 合同定义与适用范围。", "第三段 保密义务在合同终止后继续有效。", "第四段 争议提交约定法院解决。", "第二段 付款期限为60日，双方应核实发票与金额。", "第五段 全新交付安排：乙方先提交验收材料，甲方确认后付款。"]);
        WritePair(root, "page",
            ["第一条 验收用结算合同，以下数据全部虚构。", "双方按实际开票金额完成本次模拟结算。", "Page 100", "第二条 模拟交付义务和争议处理。", "合同总金额300万元，付款期限30日，签订于2026年9月。", "第三条 本合成文件仅用于验证显示规则，不涉及客户。"],
            ["第一条 验收用结算合同，以下数据全部虚构。", "双方按实际开票金额完成本次模拟结算。", "Page 101", "第二条 模拟交付义务和争议处理。", "合同总金额400万元，付款期限60日，签订于2027年9月。", "第三条 本合成文件仅用于验证显示规则，不涉及客户。"]);

        var manifest = new FixtureManifest(1, true, Names.Select(name => Inspect(root, name)).ToArray());
        using (var stream = new FileStream(Path.Combine(root, ManifestName), FileMode.CreateNew, FileAccess.Write))
            JsonSerializer.Serialize(stream, manifest, JsonOptions);
        return Verify(root);
    }

    public static FixtureManifest Verify(string directory)
    {
        var root = ValidateRoot(directory);
        var manifestPath = Path.Combine(root, ManifestName);
        RejectLink(manifestPath);
        if (new FileInfo(manifestPath).Length > 64 * 1024) throw new InvalidDataException("Manifest exceeds limit.");
        using var stream = File.OpenRead(manifestPath);
        var manifest = JsonSerializer.Deserialize<FixtureManifest>(stream)
            ?? throw new InvalidDataException("Missing manifest.");
        if (manifest.SchemaVersion != 1 || !manifest.SyntheticOnly || manifest.Files is null || manifest.Files.Any(file => file is null) ||
            !manifest.Files.Select(file => file.Name).Order(StringComparer.Ordinal).SequenceEqual(Names.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Unexpected schema or file names.");
        foreach (var expected in manifest.Files)
            if (Inspect(root, expected.Name, expected) != expected) throw new InvalidDataException("Fixture identity mismatch.");
        return manifest;
    }

    private static FixtureFile Inspect(string root, string name, FixtureFile? expected = null)
    {
        var path = Path.Combine(root, name);
        RejectLink(path);
        using var stream = File.OpenRead(path);
        if (stream.Length > 2 * 1024 * 1024) throw new InvalidDataException("Fixture exceeds limit.");
        var size = stream.Length;
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (expected is not null && (size != expected.Bytes || hash != expected.Sha256))
            throw new InvalidDataException("Fixture hash/size mismatch.");
        stream.Position = 0;
        using var document = WordprocessingDocument.Open(stream, false);
        if (new OpenXmlValidator().Validate(document).Any()) throw new InvalidDataException("Invalid synthetic DOCX.");
        var main = document.MainDocumentPart ?? throw new InvalidDataException("Missing main part.");
        var body = main.Document?.Body ?? throw new InvalidDataException("Missing body.");
        return new(name, size, hash, body.Elements<Paragraph>().Count(),
            body.Elements<Table>().Count(), main.WordprocessingCommentsPart?.Comments?.Elements<Comment>().Count() ?? 0);
    }

    private static string ValidateRoot(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("An absolute directory is required.", nameof(directory));
        var root = Path.GetFullPath(directory);
        for (var current = new DirectoryInfo(root); current is not null; current = current.Parent)
            if (current.Exists) RejectLink(current.FullName);
        return root;
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Links are not accepted.");
    }

    private static string Side(bool baseline) => baseline ? "baseline" : "current";
    private static Paragraph TextParagraph(string text) => new(new Run(new Text(text)));
    private static Paragraph FormattedParagraph(int index, bool baseline) => new(new Run(
        new RunProperties(new RunFonts { Ascii = "Arial", EastAsia = baseline ? "宋体" : "仿宋" },
            new FontSize { Val = baseline ? "24" : "22" }),
        new Text($"第{index + 1}条 付款期限为{(baseline ? 30 : 60)}日，双方应及时履行本条约定并核实付款信息。")));

    private static Table AmountTable(bool baseline) => new(
        new TableProperties(new TableWidth { Type = TableWidthUnitValues.Auto, Width = "0" }),
        new TableGrid(new GridColumn { Width = "2400" }, new GridColumn { Width = "2400" }),
        new TableRow(new TableCell(TextParagraph("项目")), new TableCell(TextParagraph(baseline ? "模板金额 100元" : "修订金额 200元"))));

    private static void AddComment(MainDocumentPart main)
    {
        var paragraph = main.Document!.Body!.Elements<Paragraph>().First();
        paragraph.PrependChild(new CommentRangeStart { Id = "0" });
        paragraph.Append(new CommentRangeEnd { Id = "0" }, new Run(new CommentReference { Id = "0" }));
        var part = main.AddNewPart<WordprocessingCommentsPart>();
        part.Comments = new Comments(new Comment(TextParagraph("纯合成批注：核对付款期限，不代表接受修改。"))
        {
            Id = "0", Author = "Synthetic Reviewer", Date = DateTime.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal)
        });
    }

    private static void WritePair(string root, string prefix, string[] baseline, string[] current)
    {
        foreach (var (side, lines) in new[] { ("baseline", baseline), ("current", current) })
            WriteDocument(root, $"{prefix}-{side}.docx", main =>
            {
                foreach (var line in lines) main.Document!.Body!.Append(TextParagraph(line));
            });
    }

    private static void WriteDocument(string root, string name, Action<MainDocumentPart> populate)
    {
        using var stream = new FileStream(Path.Combine(root, name), FileMode.CreateNew, FileAccess.ReadWrite);
        using var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        main.Document = new Document(new Body());
        populate(main);
        main.Document.Save();
    }
}
