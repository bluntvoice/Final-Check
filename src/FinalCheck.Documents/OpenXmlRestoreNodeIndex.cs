using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace FinalCheck.Documents;

internal static class OpenXmlRestoreNodeIndex
{
    public static Dictionary<string, OpenXmlElement> Build(Body body)
    {
        var nodes = new Dictionary<string, OpenXmlElement>(StringComparer.Ordinal);
        var pi = 0;
        var ti = 0;
        foreach (var child in body.ChildElements)
        {
            if (child is Paragraph p) AddParagraph(p, $"body/p[{pi++}]", nodes);
            else if (child is Table table)
            {
                var path = $"body/tbl[{ti++}]";
                nodes.Add(path, table);
                foreach (var (row, ri) in table.Elements<TableRow>().Select((r, i) => (r, i)))
                {
                    var rp = $"{path}/tr[{ri}]";
                    nodes.Add(rp, row);
                    foreach (var (cell, ci) in row.Elements<TableCell>().Select((c, i) => (c, i)))
                    {
                        var cp = $"{rp}/tc[{ci}]";
                        nodes.Add(cp, cell);
                        foreach (var (paragraph, index) in cell.Elements<Paragraph>().Select((p, i) => (p, i)))
                            AddParagraph(paragraph, $"{cp}/p[{index}]", nodes);
                    }
                }
            }
        }
        return nodes;
    }

    private static void AddParagraph(Paragraph paragraph, string path, Dictionary<string, OpenXmlElement> nodes)
    {
        nodes.Add(path, paragraph);
        foreach (var (run, index) in paragraph.Descendants<Run>().Select((r, i) => (r, i)))
            nodes.Add($"{path}/r[{index}]", run);
    }
}
