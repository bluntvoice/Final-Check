using System.Net;
using System.Text;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Documents;

namespace FinalCheck.Infrastructure;

public sealed class SnapshotHtmlPreviewRenderer : IDocumentPreviewRenderer
{
    public ValueTask<string> RenderAsync(
        DocumentSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        var builder = new StringBuilder("<article>");
        foreach (var paragraph in snapshot.Paragraphs)
        {
            builder.Append("<p>")
                .Append(WebUtility.HtmlEncode(paragraph.Text))
                .Append("</p>");
        }

        builder.Append("</article>");
        return ValueTask.FromResult(builder.ToString());
    }
}
