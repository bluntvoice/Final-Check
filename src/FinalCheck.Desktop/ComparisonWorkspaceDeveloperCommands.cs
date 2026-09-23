#if DEBUG
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FinalCheck.Desktop;

/// <summary>Generated non-sensitive fixtures and real workflow persistence, restricted to explicit Debug isolation.</summary>
internal static class ComparisonWorkspaceDeveloperCommands
{
    public static async Task<bool> RunAsync(string[] args, IPlatformStoragePaths paths, IServiceProvider services)
    {
        if (!args.Contains("--comparison-workspace-fixture", StringComparer.Ordinal)) return false;
        if (paths is not DeveloperStoragePaths) throw new ArgumentException("Comparison fixture requires isolated developer data.");
        var directory = paths.DefaultDataRoot + ".fixtures";
        if (Directory.Exists(directory)) throw new IOException("Fixture directory already exists; choose a new isolated directory.");
        Directory.CreateDirectory(directory);
        var baseline = Path.Combine(directory, "workspace-baseline.docx"); var current = Path.Combine(directory, "workspace-current.docx");
        Write(baseline, false); Write(current, true);
        var inspector = services.GetRequiredService<IComparisonFileInspector>(); var workflow = services.GetRequiredService<IComparisonWorkflowService>();
        var result = await workflow.ExecuteAsync(await workflow.ValidateAsync(await inspector.InspectAsync(baseline), await inspector.InspectAsync(current)));
        Console.WriteLine($"Fixture: {directory}; Baseline {result.Baseline.Paragraphs.Count}; Current {result.Current.Paragraphs.Count}; Changes {result.Result.Changes.Count}; Record {result.Record.RecordId}");
        return true;
    }
    private static void Write(string path, bool current)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart(); var body = new Body(); main.Document = new Document(body);
        for (var i = 1; i <= 400; i++)
        {
            if (current && i % 9 == 0) continue;
            var text = $"第{i}条 合成验证条款{i}：付款期限为{(current && i % 3 == 0 ? 60 : 30)}日，双方应核实本条款所列资料。";
            var run = new Run(new RunProperties(new RunFonts { EastAsia = "宋体", Ascii = "Arial" }, new FontSize { Val = "24" }), new Text(text));
            if (current && i % 7 == 0) run.RunProperties!.Append(new Bold());
            var paragraph = new Paragraph(run); body.Append(paragraph);
            if (current && i == 3)
            {
                paragraph.PrependChild(new CommentRangeStart { Id = "1" }); paragraph.Append(new CommentRangeEnd { Id = "1" }, new Run(new CommentReference { Id = "1" }));
                var comments = main.AddNewPart<WordprocessingCommentsPart>();
                comments.Comments = new Comments(new Comment(new Paragraph(new Run(new Text("合成批注：请核实付款期限，仅用于工作台验收。"))))
                    { Id = "1", Author = "合成测试作者", Date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
                comments.Comments.Save();
            }
            if (current && i % 5 == 0) body.Append(new Paragraph(new Run(new Text($"新增合成条款{i}，不得覆盖或丢失此处新增内容。"))));
            if (current && i == 140)
                for (var extra = 0; extra < 80; extra++) body.Append(new Paragraph(new Run(new Text($"新增附录{extra}：这一大段无直接基准对应，联动应保守保持另一侧。"))));
        }
        main.Document.Save();
    }
}
#endif
