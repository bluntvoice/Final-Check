using FinalCheck.Core.Documents;

namespace FinalCheck.Core.Tests;

public sealed class DocumentSnapshotTests
{
    [Fact]
    public void EmptySnapshotUsesCurrentSchemaVersion()
    {
        Assert.Equal(DocumentSnapshot.CurrentSchemaVersion, DocumentSnapshot.Empty.SnapshotSchemaVersion);
        Assert.Empty(DocumentSnapshot.Empty.Paragraphs);
        Assert.Empty(DocumentSnapshot.Empty.Tables);
    }

    [Fact]
    public void NodeMappingRetainsCrossDocumentIdentity()
    {
        var mapping = new DocumentNodeMapping(
            new DocumentNodeReference("template:p1", DocumentNodeKind.Paragraph, 1),
            new DocumentNodeReference("revised:p2", DocumentNodeKind.Paragraph, 2),
            0.95);

        Assert.Equal("template:p1", mapping.TemplateNode.NodeId);
        Assert.Equal("revised:p2", mapping.RevisedNode.NodeId);
        Assert.InRange(mapping.Confidence, 0, 1);
    }
}
