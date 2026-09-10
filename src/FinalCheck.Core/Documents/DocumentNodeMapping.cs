namespace FinalCheck.Core.Documents;

public enum DocumentNodeKind
{
    Section,
    Paragraph,
    Run,
    Table,
    Row,
    Cell,
}

public sealed record DocumentNodeReference(
    string NodeId,
    DocumentNodeKind Kind,
    int Position);

public sealed record DocumentNodeMapping(
    DocumentNodeReference TemplateNode,
    DocumentNodeReference RevisedNode,
    double Confidence);
