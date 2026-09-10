using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Core.Documents;

namespace FinalCheck.Documents;

internal sealed class OpenXmlParserContext
{
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private readonly WordprocessingDocument _document;
    private readonly MainDocumentPart _mainPart;
    private readonly Body _body;
    private readonly CancellationToken _cancellationToken;
    private readonly IProgress<DocumentParseProgress>? _progress;
    private readonly string _sourcePart;
    private readonly string _sha256;
    private readonly List<DocumentParseDiagnostic> _diagnostics = [];
    private readonly Dictionary<OpenXmlElement, DocumentNodeIdentitySnapshot> _nodes = [];
    private readonly Dictionary<string, CommentAnchorBuilder> _commentAnchors = new(StringComparer.Ordinal);
    private readonly List<DocumentHyperlinkSnapshot> _hyperlinks = [];
    private readonly OpenXmlFormattingResolver _formattingResolver;
    private DocumentNumberingSnapshot _numbering = DocumentNumberingSnapshot.Empty;

    public OpenXmlParserContext(
        WordprocessingDocument document,
        string sha256,
        CancellationToken cancellationToken,
        IProgress<DocumentParseProgress>? progress)
    {
        _document = document;
        _cancellationToken = cancellationToken;
        _progress = progress;
        _sha256 = sha256;
        _mainPart = document.MainDocumentPart
            ?? throw new DocumentParseException(DocumentParseErrorKind.InvalidPackage, "The DOCX package has no main document part.");
        var mainDocument = _mainPart.Document
            ?? throw new DocumentParseException(DocumentParseErrorKind.InvalidPackage, "The DOCX main document part is empty.");
        _body = mainDocument.Body
            ?? throw new DocumentParseException(DocumentParseErrorKind.InvalidPackage, "The DOCX main document has no body.");
        _sourcePart = _mainPart.Uri.ToString();

        _progress?.Report(new DocumentParseProgress(DocumentParseStage.ReadingStyles, 0, null));
        _formattingResolver = new OpenXmlFormattingResolver(
            _mainPart.StyleDefinitionsPart?.Styles,
            _diagnostics);
    }

    public DocumentSnapshot Parse()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        _progress?.Report(new DocumentParseProgress(DocumentParseStage.ReadingNumbering, 0, null));
        _numbering = ParseNumbering();

        var bodyChildren = _body.ChildElements.ToArray();
        _progress?.Report(new DocumentParseProgress(DocumentParseStage.ReadingDocument, 0, bodyChildren.Length));
        var paragraphs = new List<DocumentParagraphSnapshot>();
        var tables = new List<DocumentTableSnapshot>();
        var paragraphIndex = 0;
        var tableIndex = 0;
        for (var sourceIndex = 0; sourceIndex < bodyChildren.Length; sourceIndex++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            switch (bodyChildren[sourceIndex])
            {
                case Paragraph paragraph:
                    paragraphs.Add(ParseParagraph(paragraph, null, $"body/p[{paragraphIndex}]", paragraphIndex, sourceIndex));
                    paragraphIndex++;
                    break;
                case Table table:
                    _progress?.Report(new DocumentParseProgress(DocumentParseStage.ReadingTables, tableIndex, null));
                    tables.Add(ParseTable(table, null, $"body/tbl[{tableIndex}]", tableIndex, sourceIndex));
                    tableIndex++;
                    break;
                case SectionProperties:
                    break;
                default:
                    AddUnsupportedElement(bodyChildren[sourceIndex], null, _sourcePart, true);
                    break;
            }

            _progress?.Report(new DocumentParseProgress(DocumentParseStage.ReadingDocument, sourceIndex + 1, bodyChildren.Length));
        }

        DetectUnsupportedBodyContent();
        var sections = ParseSections();
        var revisions = ParseRevisions();
        _progress?.Report(new DocumentParseProgress(DocumentParseStage.ReadingComments, 0, null));
        var comments = ParseComments();
        var headerFooters = ParseHeadersAndFooters();
        var protection = ParseProtection();
        _progress?.Report(new DocumentParseProgress(DocumentParseStage.BuildingSnapshot, bodyChildren.Length, bodyChildren.Length));

        var parseStatus = _diagnostics.Any(diagnostic => diagnostic.Severity != DocumentDiagnosticSeverity.Info)
            ? DocumentParseStatus.Partial
            : DocumentParseStatus.Complete;
        var snapshot = new DocumentSnapshot(
            DocumentSnapshot.CurrentSchemaVersion,
            ParseMetadata(bodyChildren.Length),
            sections,
            paragraphs,
            tables,
            revisions,
            comments,
            _formattingResolver.StyleSnapshots,
            _numbering,
            _formattingResolver.Defaults,
            _hyperlinks,
            protection,
            headerFooters,
            parseStatus,
            _diagnostics.ToArray());
        _progress?.Report(new DocumentParseProgress(DocumentParseStage.Completed, bodyChildren.Length, bodyChildren.Length));
        return snapshot;
    }

    private DocumentParagraphSnapshot ParseParagraph(
        Paragraph paragraph,
        DocumentNodeIdentitySnapshot? parent,
        string structuralPath,
        int index,
        int sourceIndex)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var identity = CreateIdentity(structuralPath, parent?.NodeId, DocumentNodeKind.Paragraph, structuralPath, _sourcePart, sourceIndex);
        _nodes[paragraph] = identity;

        var runs = paragraph.Descendants<Run>()
            .Select((run, runIndex) => ParseRun(paragraph, run, identity, $"{structuralPath}/r[{runIndex}]", runIndex))
            .ToArray();
        RegisterCommentAnchors(paragraph, identity);
        RegisterHyperlinks(paragraph, identity);

        var rawText = string.Concat(runs.Select(run => run.RawText));
        var displayText = string.Concat(runs.Select(run => run.DisplayText));
        var directFormatting = OpenXmlFormattingReader.ReadParagraphFormat(paragraph.ParagraphProperties);
        var numbering = ParseParagraphNumbering(paragraph, identity.NodeId);
        return new DocumentParagraphSnapshot(
            identity,
            index,
            rawText,
            displayText,
            string.IsNullOrEmpty(displayText),
            paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value,
            runs,
            directFormatting,
            _formattingResolver.ResolveParagraph(paragraph, identity.NodeId, _sourcePart),
            numbering);
    }

    private DocumentRunSnapshot ParseRun(
        Paragraph paragraph,
        Run run,
        DocumentNodeIdentitySnapshot paragraphIdentity,
        string structuralPath,
        int index)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var identity = CreateIdentity(
            structuralPath,
            paragraphIdentity.NodeId,
            DocumentNodeKind.Run,
            structuralPath,
            _sourcePart,
            index);
        _nodes[run] = identity;

        var isDeletedRevision = run.Ancestors<DeletedRun>().Any();
        var content = run.Descendants()
            .Where(IsRunContent)
            .Select((element, contentIndex) => ParseRunContent(element, contentIndex, isDeletedRevision, identity.NodeId))
            .ToArray();
        var rawText = string.Concat(content.Select(item => item.RawText));
        var displayText = string.Concat(content.Select(item => item.DisplayText));
        return new DocumentRunSnapshot(
            identity,
            index,
            paragraphIdentity.NodeId,
            rawText,
            displayText,
            string.IsNullOrEmpty(displayText),
            content,
            OpenXmlFormattingReader.ReadCharacterFormat(run.RunProperties),
            _formattingResolver.ResolveRun(paragraph, run, identity.NodeId, _sourcePart));
    }

    private RunContentSnapshot ParseRunContent(OpenXmlElement element, int index, bool isDeletedRevision, string runNodeId)
    {
        return element switch
        {
            Text text => new RunContentSnapshot(RunContentKind.Text, text.Text, isDeletedRevision ? string.Empty : text.Text, index),
            DeletedText text => new RunContentSnapshot(RunContentKind.DeletedText, text.Text, string.Empty, index),
            TabChar => new RunContentSnapshot(RunContentKind.Tab, "\t", isDeletedRevision ? string.Empty : "\t", index),
            Break => new RunContentSnapshot(RunContentKind.Break, "\n", isDeletedRevision ? string.Empty : "\n", index),
            CarriageReturn => new RunContentSnapshot(RunContentKind.CarriageReturn, "\n", isDeletedRevision ? string.Empty : "\n", index),
            FieldCode fieldCode => new RunContentSnapshot(RunContentKind.FieldCode, fieldCode.Text, string.Empty, index),
            _ => UnsupportedRunContent(element, index, runNodeId),
        };
    }

    private RunContentSnapshot UnsupportedRunContent(OpenXmlElement element, int index, string runNodeId)
    {
        AddUnsupportedElement(element, runNodeId, _sourcePart, true);
        return new RunContentSnapshot(RunContentKind.Unsupported, element.InnerText, string.Empty, index);
    }

    private DocumentTableSnapshot ParseTable(
        Table table,
        DocumentNodeIdentitySnapshot? parent,
        string structuralPath,
        int index,
        int sourceIndex)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var identity = CreateIdentity(structuralPath, parent?.NodeId, DocumentNodeKind.Table, structuralPath, _sourcePart, sourceIndex);
        _nodes[table] = identity;
        var rows = table.Elements<TableRow>()
            .Select((row, rowIndex) => ParseRow(row, identity, $"{structuralPath}/tr[{rowIndex}]", rowIndex))
            .ToArray();
        var format = OpenXmlFormattingReader.ReadTableFormat(table.TableProperties);
        if (!string.IsNullOrWhiteSpace(format.StyleId) &&
            _formattingResolver.StyleSnapshots.All(style => !string.Equals(style.StyleId, format.StyleId, StringComparison.Ordinal)))
        {
            _diagnostics.Add(new DocumentParseDiagnostic(
                DocumentDiagnosticSeverity.Warning,
                "BrokenTableStyleReference",
                $"Table style '{format.StyleId}' could not be resolved.",
                identity.NodeId,
                _sourcePart,
                false));
        }

        return new DocumentTableSnapshot(identity, index, rows, format);
    }

    private DocumentTableRowSnapshot ParseRow(
        TableRow row,
        DocumentNodeIdentitySnapshot tableIdentity,
        string structuralPath,
        int index)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var identity = CreateIdentity(structuralPath, tableIdentity.NodeId, DocumentNodeKind.Row, structuralPath, _sourcePart, index);
        _nodes[row] = identity;
        var cells = new List<DocumentTableCellSnapshot>();
        var columnIndex = 0;
        var cellIndex = 0;
        foreach (var cell in row.Elements<TableCell>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var parsed = ParseCell(cell, identity, $"{structuralPath}/tc[{cellIndex}]", cellIndex, index, columnIndex);
            cells.Add(parsed);
            columnIndex += Math.Max(1, parsed.DirectFormatting.GridSpan ?? 1);
            cellIndex++;
        }

        var height = row.TableRowProperties?.GetFirstChild<TableRowHeight>();
        return new DocumentTableRowSnapshot(
            identity,
            index,
            cells,
            height?.Val?.Value,
            height?.HeightType?.ToString());
    }

    private DocumentTableCellSnapshot ParseCell(
        TableCell cell,
        DocumentNodeIdentitySnapshot rowIdentity,
        string structuralPath,
        int index,
        int rowIndex,
        int columnIndex)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var identity = CreateIdentity(structuralPath, rowIdentity.NodeId, DocumentNodeKind.Cell, structuralPath, _sourcePart, index);
        _nodes[cell] = identity;
        var paragraphs = cell.Elements<Paragraph>()
            .Select((paragraph, paragraphIndex) => ParseParagraph(
                paragraph,
                identity,
                $"{structuralPath}/p[{paragraphIndex}]",
                paragraphIndex,
                paragraphIndex))
            .ToArray();
        var nestedTableCount = cell.Elements<Table>().Count();
        if (nestedTableCount > 0)
        {
            _diagnostics.Add(new DocumentParseDiagnostic(
                DocumentDiagnosticSeverity.Warning,
                "UnsupportedNestedTable",
                $"The cell contains {nestedTableCount} nested table(s); their structure was not expanded in schema v2.",
                identity.NodeId,
                _sourcePart,
                true));
        }

        return new DocumentTableCellSnapshot(
            identity,
            index,
            rowIndex,
            columnIndex,
            string.Join("\n", paragraphs.Select(paragraph => paragraph.DisplayText)),
            paragraphs,
            OpenXmlFormattingReader.ReadTableCellFormat(cell.TableCellProperties),
            nestedTableCount);
    }

    private DocumentNumberingSnapshot ParseNumbering()
    {
        var root = _mainPart.NumberingDefinitionsPart?.Numbering;
        if (root is null)
        {
            return DocumentNumberingSnapshot.Empty;
        }

        var abstractDefinitions = root.Elements<AbstractNum>()
            .Where(item => item.AbstractNumberId?.Value is not null)
            .Select(item => new AbstractNumberingSnapshot(
                item.AbstractNumberId!.Value,
                item.Elements<Level>()
                    .Where(level => level.LevelIndex?.Value is not null)
                    .Select(level => new NumberingLevelSnapshot(
                        level.LevelIndex!.Value,
                        level.NumberingFormat?.Val?.ToString(),
                        level.LevelText?.Val?.Value,
                        level.StartNumberingValue?.Val?.Value))
                    .ToArray()))
            .ToArray();
        var instances = root.Elements<NumberingInstance>()
            .Where(item => item.NumberID?.Value is not null && item.AbstractNumId?.Val?.Value is not null)
            .Select(item => new NumberingInstanceSnapshot(item.NumberID!.Value, item.AbstractNumId!.Val!.Value))
            .ToArray();
        return new DocumentNumberingSnapshot(abstractDefinitions, instances);
    }

    private ParagraphNumberingReferenceSnapshot? ParseParagraphNumbering(Paragraph paragraph, string nodeId)
    {
        var properties = paragraph.ParagraphProperties?.NumberingProperties;
        var numId = properties?.NumberingId?.Val?.Value;
        var levelIndex = properties?.NumberingLevelReference?.Val?.Value;
        if (numId is null && levelIndex is null)
        {
            return null;
        }

        if (numId is null || levelIndex is null)
        {
            _diagnostics.Add(new DocumentParseDiagnostic(
                DocumentDiagnosticSeverity.Warning,
                "InvalidNumberingReference",
                "The paragraph numbering reference is missing numId or ilvl.",
                nodeId,
                _sourcePart,
                false));
            return new ParagraphNumberingReferenceSnapshot(numId ?? -1, levelIndex ?? -1, null, null, null, null);
        }

        var instance = _numbering.Instances.FirstOrDefault(item => item.NumberingId == numId.Value);
        var definition = instance is null
            ? null
            : _numbering.AbstractDefinitions.FirstOrDefault(item => item.AbstractNumberingId == instance.AbstractNumberingId);
        var level = definition?.Levels.FirstOrDefault(item => item.LevelIndex == levelIndex.Value);
        if (instance is null || level is null)
        {
            _diagnostics.Add(new DocumentParseDiagnostic(
                DocumentDiagnosticSeverity.Warning,
                "InvalidNumberingReference",
                $"Numbering reference numId={numId.Value}, ilvl={levelIndex.Value} could not be resolved.",
                nodeId,
                _sourcePart,
                false));
        }

        return new ParagraphNumberingReferenceSnapshot(
            numId.Value,
            levelIndex.Value,
            instance?.AbstractNumberingId,
            level?.NumberFormat,
            level?.LevelText,
            level?.StartValue);
    }

    private List<DocumentRevisionSnapshot> ParseRevisions()
    {
        var results = new List<DocumentRevisionSnapshot>();
        AddRevisions<InsertedRun>(results, DocumentRevisionKind.Insert);
        AddRevisions<DeletedRun>(results, DocumentRevisionKind.Delete);
        AddRevisions<RunPropertiesChange>(results, DocumentRevisionKind.RunPropertyChange);
        AddRevisions<ParagraphPropertiesChange>(results, DocumentRevisionKind.ParagraphPropertyChange);
        AddRevisions<TablePropertiesChange>(results, DocumentRevisionKind.TablePropertyChange);
        return results;
    }

    private void AddRevisions<T>(List<DocumentRevisionSnapshot> results, DocumentRevisionKind kind)
        where T : OpenXmlElement
    {
        foreach (var revision in _body.Descendants<T>())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var index = results.Count;
            var paragraphNode = FindAncestorNode<Paragraph>(revision);
            var runNode = FindAncestorNode<Run>(revision) ?? revision.Descendants<Run>().Select(run => FindNode(run)).FirstOrDefault(node => node is not null);
            var affectedNode = runNode ?? paragraphNode ?? FindNearestNode(revision);
            var identity = CreateIdentity(
                $"revisions/{kind}[{index}]",
                affectedNode?.NodeId,
                affectedNode?.Kind ?? DocumentNodeKind.Run,
                $"revisions/{kind}[{index}]",
                _sourcePart,
                index);
            var text = string.Concat(revision.Descendants().Select(GetRevisionText));
            results.Add(new DocumentRevisionSnapshot(
                identity,
                ReadAttribute(revision, "id") ?? string.Empty,
                kind,
                text,
                ReadAttribute(revision, "author"),
                ParseDate(ReadAttribute(revision, "date")),
                affectedNode?.NodeId,
                paragraphNode?.NodeId,
                runNode?.NodeId,
                true,
                kind == DocumentRevisionKind.RunPropertyChange
                    ? OpenXmlFormattingReader.ReadCharacterFormat(revision.FirstChild)
                    : null,
                kind == DocumentRevisionKind.ParagraphPropertyChange
                    ? OpenXmlFormattingReader.ReadParagraphFormat(revision.FirstChild)
                    : null,
                kind == DocumentRevisionKind.TablePropertyChange
                    ? OpenXmlFormattingReader.ReadTableFormat(revision.FirstChild)
                    : null));
        }
    }

    private DocumentCommentSnapshot[] ParseComments()
    {
        var commentRoot = _mainPart.WordprocessingCommentsPart?.Comments;
        if (commentRoot is null)
        {
            return [];
        }

        return commentRoot.Elements<Comment>()
            .Select(comment =>
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var commentId = comment.Id?.Value ?? string.Empty;
                _commentAnchors.TryGetValue(commentId, out var anchor);
                var isAnchored = anchor is not null && anchor.HasRangeOrReference;
                if (!isAnchored)
                {
                    _diagnostics.Add(new DocumentParseDiagnostic(
                        DocumentDiagnosticSeverity.Warning,
                        "UnresolvedCommentAnchor",
                        $"Comment '{commentId}' was preserved but could not be associated with document content.",
                        null,
                        _sourcePart,
                        false));
                }

                return new DocumentCommentSnapshot(
                    commentId,
                    ExtractBasicText(comment),
                    comment.Author?.Value,
                    comment.Initials?.Value,
                    ToUtc(comment.Date?.Value),
                    anchor?.StartNodeId,
                    anchor?.EndNodeId,
                    anchor?.ParagraphNodeId,
                    anchor?.ReferenceRunNodeId,
                    isAnchored);
            })
            .ToArray();
    }

    private void RegisterCommentAnchors(Paragraph paragraph, DocumentNodeIdentitySnapshot paragraphIdentity)
    {
        var elements = paragraph.Descendants().ToArray();
        for (var index = 0; index < elements.Length; index++)
        {
            switch (elements[index])
            {
                case CommentRangeStart start when start.Id?.Value is { } startId:
                    var nextRun = elements.Skip(index + 1).OfType<Run>().FirstOrDefault();
                    var startAnchor = GetCommentAnchor(startId);
                    startAnchor.ParagraphNodeId ??= paragraphIdentity.NodeId;
                    startAnchor.StartNodeId = FindNode(nextRun)?.NodeId ?? paragraphIdentity.NodeId;
                    break;
                case CommentRangeEnd end when end.Id?.Value is { } endId:
                    var previousRun = elements.Take(index).OfType<Run>().LastOrDefault();
                    var endAnchor = GetCommentAnchor(endId);
                    endAnchor.ParagraphNodeId ??= paragraphIdentity.NodeId;
                    endAnchor.EndNodeId = FindNode(previousRun)?.NodeId ?? paragraphIdentity.NodeId;
                    break;
                case CommentReference reference when reference.Id?.Value is { } referenceId:
                    var referenceRun = reference.Ancestors<Run>().FirstOrDefault();
                    var referenceAnchor = GetCommentAnchor(referenceId);
                    referenceAnchor.ParagraphNodeId ??= paragraphIdentity.NodeId;
                    referenceAnchor.ReferenceRunNodeId = FindNode(referenceRun)?.NodeId;
                    break;
            }
        }
    }

    private void RegisterHyperlinks(Paragraph paragraph, DocumentNodeIdentitySnapshot paragraphIdentity)
    {
        var relationships = _mainPart.HyperlinkRelationships.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var (hyperlink, index) in paragraph.Descendants<Hyperlink>().Select((item, index) => (item, index)))
        {
            var relationshipId = hyperlink.Id?.Value;
            relationships.TryGetValue(relationshipId ?? string.Empty, out var relationship);
            _hyperlinks.Add(new DocumentHyperlinkSnapshot(
                $"{paragraphIdentity.StructuralPath}/hyperlink[{index}]",
                paragraphIdentity.NodeId,
                ExtractBasicText(hyperlink),
                relationshipId,
                relationship?.Uri.ToString(),
                relationship?.IsExternal ?? false));
        }
    }

    private DocumentSectionSnapshot[] ParseSections()
    {
        return _body.Descendants<SectionProperties>()
            .Select((section, index) =>
            {
                var path = $"body/sectPr[{index}]";
                var identity = CreateIdentity(path, null, DocumentNodeKind.Section, path, _sourcePart, index);
                _nodes[section] = identity;
                var pageSize = section.GetFirstChild<PageSize>();
                var margins = section.GetFirstChild<PageMargin>();
                return new DocumentSectionSnapshot(
                    identity,
                    index,
                    pageSize?.Width?.Value,
                    pageSize?.Height?.Value,
                    pageSize?.Orient?.ToString(),
                    margins?.Top?.Value,
                    margins?.Right?.Value,
                    margins?.Bottom?.Value,
                    margins?.Left?.Value);
            })
            .ToArray();
    }

    private List<HeaderFooterSnapshot> ParseHeadersAndFooters()
    {
        var results = new List<HeaderFooterSnapshot>();
        foreach (var part in _mainPart.HeaderParts)
        {
            results.Add(new HeaderFooterSnapshot(
                "Header",
                _mainPart.GetIdOfPart(part),
                part.Uri.ToString(),
                part.Header is null ? string.Empty : ExtractBasicText(part.Header)));
        }

        foreach (var part in _mainPart.FooterParts)
        {
            results.Add(new HeaderFooterSnapshot(
                "Footer",
                _mainPart.GetIdOfPart(part),
                part.Uri.ToString(),
                part.Footer is null ? string.Empty : ExtractBasicText(part.Footer)));
        }

        return results;
    }

    private DocumentProtectionSnapshot ParseProtection()
    {
        var protection = _mainPart.DocumentSettingsPart?.Settings?.GetFirstChild<DocumentProtection>();
        if (protection is null)
        {
            return DocumentProtectionSnapshot.None;
        }

        return new DocumentProtectionSnapshot(
            true,
            ReadOnOffAttribute(protection, "enforcement"),
            ReadAttribute(protection, "edit"),
            ReadAttribute(protection, "algorithmName"),
            ReadAttribute(protection, "cryptAlgorithmSid"),
            ReadAttribute(protection, "cryptProviderType"),
            ReadAttribute(protection, "cryptSpinCount"),
            ReadAttribute(protection, "hash"),
            ReadAttribute(protection, "salt"));
    }

    private DocumentMetadataSnapshot ParseMetadata(int bodyElementCount)
    {
        var properties = _document.PackageProperties;
        return new DocumentMetadataSnapshot(
            properties.Title,
            properties.Creator,
            ToUtc(properties.Created),
            ToUtc(properties.Modified),
            _sourcePart,
            bodyElementCount,
            _sha256);
    }

    private void DetectUnsupportedBodyContent()
    {
        foreach (var drawing in _body.Descendants<Drawing>())
        {
            AddUnsupportedElement(drawing, FindNearestNode(drawing)?.NodeId, _sourcePart, true, "UnsupportedDrawing");
        }

        foreach (var bookmark in _body.Descendants<BookmarkStart>())
        {
            _diagnostics.Add(new DocumentParseDiagnostic(
                DocumentDiagnosticSeverity.Info,
                "BookmarkPreservedAsBoundary",
                $"Bookmark '{bookmark.Name?.Value}' was detected; bookmark semantics are not expanded in schema v2.",
                FindNearestNode(bookmark)?.NodeId,
                _sourcePart,
                false));
        }

        foreach (var field in _body.Descendants<FieldCode>())
        {
            _diagnostics.Add(new DocumentParseDiagnostic(
                DocumentDiagnosticSeverity.Info,
                "FieldCodePreserved",
                "A field code was detected; its instruction is stored as run content but field semantics are not evaluated.",
                FindNearestNode(field)?.NodeId,
                _sourcePart,
                false));
        }
    }

    private void AddUnsupportedElement(
        OpenXmlElement element,
        string? nodeId,
        string sourcePart,
        bool contentWasSkipped,
        string code = "UnsupportedElement")
    {
        _diagnostics.Add(new DocumentParseDiagnostic(
            DocumentDiagnosticSeverity.Warning,
            code,
            $"Element '{element.LocalName}' is not fully represented in snapshot schema v2.",
            nodeId,
            sourcePart,
            contentWasSkipped));
    }

    private DocumentNodeIdentitySnapshot? FindNearestNode(OpenXmlElement? element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (_nodes.TryGetValue(current, out var identity))
            {
                return identity;
            }
        }

        return null;
    }

    private DocumentNodeIdentitySnapshot? FindAncestorNode<T>(OpenXmlElement element)
        where T : OpenXmlElement
    {
        var ancestor = element.Ancestors<T>().FirstOrDefault();
        return FindNode(ancestor);
    }

    private DocumentNodeIdentitySnapshot? FindNode(OpenXmlElement? element) =>
        element is not null && _nodes.TryGetValue(element, out var identity) ? identity : null;

    private CommentAnchorBuilder GetCommentAnchor(string commentId)
    {
        if (!_commentAnchors.TryGetValue(commentId, out var anchor))
        {
            anchor = new CommentAnchorBuilder();
            _commentAnchors[commentId] = anchor;
        }

        return anchor;
    }

    private static DocumentNodeIdentitySnapshot CreateIdentity(
        string nodeId,
        string? parentNodeId,
        DocumentNodeKind kind,
        string structuralPath,
        string sourcePart,
        int sourceIndex) => new(nodeId, parentNodeId, kind, structuralPath, sourcePart, sourceIndex);

    private static bool IsRunContent(OpenXmlElement element) => element is
        Text or DeletedText or TabChar or Break or CarriageReturn or FieldCode;

    private static string GetRevisionText(OpenXmlElement element) => element switch
    {
        Text text => text.Text,
        DeletedText text => text.Text,
        TabChar => "\t",
        Break or CarriageReturn => "\n",
        _ => string.Empty,
    };

    private static string ExtractBasicText(OpenXmlElement element) => string.Concat(
        element.Descendants().Select(GetRevisionText));

    private static string? ReadAttribute(OpenXmlElement element, string localName)
    {
        var attribute = element.GetAttributes().FirstOrDefault(candidate =>
            string.Equals(candidate.LocalName, localName, StringComparison.Ordinal) &&
            string.Equals(candidate.NamespaceUri, WordprocessingNamespace, StringComparison.Ordinal));
        return string.IsNullOrEmpty(attribute.Value) ? null : attribute.Value;
    }

    private static bool ReadOnOffAttribute(OpenXmlElement element, string localName)
    {
        var value = ReadAttribute(element, localName);
        return value is not null && value is not ("0" or "false" or "off");
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;

    private static DateTimeOffset? ToUtc(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }

    private sealed class CommentAnchorBuilder
    {
        public string? StartNodeId { get; set; }

        public string? EndNodeId { get; set; }

        public string? ParagraphNodeId { get; set; }

        public string? ReferenceRunNodeId { get; set; }

        public bool HasRangeOrReference => StartNodeId is not null || EndNodeId is not null || ReferenceRunNodeId is not null;
    }
}
