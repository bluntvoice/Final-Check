using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Formatting;

namespace FinalCheck.Documents;

public sealed class OpenXmlFormatRestoreRenderer(IDocumentParser parser) : IFormatRestoreRenderer
{
    internal const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public async ValueTask<FormatRestoreRenderResult> RevertAsync(Stream source, string expectedSha256,
        IReadOnlyList<FormatRestoreMutation> mutations, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        source.Position = 0;
        using var copy = new MemoryStream();
        await source.CopyToAsync(copy, cancellationToken);
        var original = copy.ToArray();
        var before = await parser.ParseAsync(copy, cancellationToken: cancellationToken);
        if (before.Metadata.Sha256 != expectedSha256) throw new InvalidDataException("UndoHashMismatch.");
        copy.Position = 0;
        using (var document = WordprocessingDocument.Open(copy, true))
        {
            var nodes = OpenXmlRestoreNodeIndex.Build(document.MainDocumentPart!.Document!.Body!);
            foreach (var mutation in mutations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!nodes.TryGetValue(mutation.NodeId, out var node) ||
                    (node is Run ? "rPr" : node is Paragraph ? "pPr" : node is Table ? "tblPr" : node is TableRow ? "trPr" : node is TableCell ? "tcPr" : "") != mutation.PropertyKind)
                    throw new InvalidDataException("Undo mutation does not refer to a supported properties node.");
                var existing = node.ChildElements.FirstOrDefault(e => e.LocalName == mutation.PropertyKind && e.NamespaceUri == W);
                if (existing?.OuterXml != mutation.AfterPropertiesXml) throw new InvalidDataException("Undo properties do not match saved after state.");
                existing?.Remove();
                if (mutation.BeforePropertiesXml is { } xml)
                {
                    OpenXmlElement properties = mutation.PropertyKind switch
                    {
                        "rPr" => new RunProperties(xml), "pPr" => new ParagraphProperties(xml), "tblPr" => new TableProperties(xml),
                        "trPr" => new TableRowProperties(xml), "tcPr" => new TableCellProperties(xml), _ => throw new InvalidDataException(),
                    };
                    ((OpenXmlCompositeElement)node).AddChild(properties, true);
                }
            }
            document.MainDocumentPart!.Document!.Save();
        }
        cancellationToken.ThrowIfCancellationRequested();
        var after = await parser.ParseAsync(copy, cancellationToken: cancellationToken);
        ValidatePreservation(original, copy.ToArray(), before, after);
        return new(copy.ToArray(), after, mutations.Select(m => m with { BeforePropertiesXml = m.AfterPropertiesXml, AfterPropertiesXml = m.BeforePropertiesXml }).ToArray(), []);
    }

    public async ValueTask<FormatRestoreRenderResult> RenderAsync(Stream source, FormatRestorePlan plan,
        FormatRestoreScope? scope = null, IProgress<FormatRestoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);
        if (!source.CanRead || !source.CanSeek) throw new ArgumentException("A readable seekable source is required.");
        if (plan.SchemaVersion != FormatRestorePlan.CurrentSchemaVersion) throw new InvalidDataException("Unsupported restore plan schema.");
        if (!double.IsFinite(plan.Policy.MinimumScore) || plan.Policy.MinimumScore is < 0 or > 1 ||
            plan.RestoreItems.Any(i => i.Eligibility == FormatRestoreEligibility.Eligible &&
                (i.Mapping is null || i.Mapping.Confidence is not (ComparisonConfidenceLevel.Exact or ComparisonConfidenceLevel.High) ||
                 !double.IsFinite(i.Confidence) || i.Confidence < plan.Policy.MinimumScore || i.Confidence > 1 ||
                 !double.IsFinite(i.Mapping.Score) || i.Mapping.Score < plan.Policy.MinimumScore || i.Mapping.Score > 1)))
            throw new InvalidDataException("Eligible restore items require trusted comparison evidence.");
        scope ??= new();
        var selected = SelectItems(plan, scope);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new(FormatRestoreStage.PreparingWorkingCopy, 0, selected.Count));
        source.Position = 0;
        using var copy = new MemoryStream();
        await source.CopyToAsync(copy, cancellationToken);
        var beforeBytes = copy.ToArray();
        var before = await parser.ParseAsync(copy, cancellationToken: cancellationToken);
        if (plan.SourceSha256.Length != 64 || before.Metadata.Sha256 != plan.SourceSha256)
            throw new InvalidDataException("Stale restore plan: source hash mismatch.");
        var mutations = new List<FormatRestoreMutation>();
        var diagnostics = new List<FormatRestoreDiagnostic>();
        copy.Position = 0;
        using (var document = WordprocessingDocument.Open(copy, true))
        {
            var main = document.MainDocumentPart ?? throw new InvalidDataException("No main document part.");
            if (main.Document!.Descendants().Any(e => e.NamespaceUri == W && e.LocalName.EndsWith("PrChange", StringComparison.Ordinal)))
                diagnostics.Add(new("ExistingFormatRevisionPreserved", null, "Existing format revision subtrees are protected; no revision acceptance is performed."));
            var nodes = OpenXmlRestoreNodeIndex.Build(main.Document!.Body ?? throw new InvalidDataException("No document body."));
            var resolver = new OpenXmlFormattingResolver(main.StyleDefinitionsPart?.Styles, []);
            foreach (var item in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Eligibility != FormatRestoreEligibility.Eligible)
                {
                    diagnostics.Add(new("LowConfidenceMapping", item.CurrentNodeId, "Non-eligible item skipped."));
                    continue;
                }
                if (!nodes.TryGetValue(item.CurrentNodeId, out var element)) throw new InvalidDataException("Restore node is missing.");
                if (element is Run run && item.Category == FormatRestoreCategory.Character && item.TargetFormatting.Character is { } target)
                {
                    progress?.Report(new(FormatRestoreStage.ApplyingCharacterFormatting, mutations.Count, selected.Count));
                    var old = run.RunProperties?.OuterXml;
                    ApplyCharacter(run, target, resolver);
                    var updated = run.RunProperties?.OuterXml;
                    if (old != updated) mutations.Add(new(item.CurrentNodeId, "rPr", old, updated));
                }
                else if (element is Paragraph paragraph && item.Category == FormatRestoreCategory.Paragraph && item.TargetFormatting.Paragraph is { } paragraphTarget)
                {
                    progress?.Report(new(FormatRestoreStage.ApplyingParagraphFormatting, mutations.Count, selected.Count));
                    var old = paragraph.ParagraphProperties?.OuterXml;
                    ApplyParagraph(paragraph, paragraphTarget, item.TargetFormatting.ParagraphStyleId, main.StyleDefinitionsPart?.Styles, resolver, diagnostics, item.CurrentNodeId);
                    var updated = paragraph.ParagraphProperties?.OuterXml;
                    if (old != updated) mutations.Add(new(item.CurrentNodeId, "pPr", old, updated));
                }
                else if (element is Table or TableCell or TableRow && item.Category is FormatRestoreCategory.Table or FormatRestoreCategory.Cell)
                {
                    progress?.Report(new(FormatRestoreStage.ApplyingTableFormatting, mutations.Count, selected.Count));
                    var kind = element is Table ? "tblPr" : element is TableCell ? "tcPr" : "trPr";
                    var old = element.ChildElements.FirstOrDefault(e => e.LocalName == kind)?.OuterXml;
                    ApplyTableProperties(element, item.TargetFormatting, kind);
                    var updated = element.ChildElements.FirstOrDefault(e => e.LocalName == kind)?.OuterXml;
                    if (old != updated) mutations.Add(new(item.CurrentNodeId, kind, old, updated));
                }
                else diagnostics.Add(new("UnsupportedRestoreCategory", item.CurrentNodeId, "This restore category has no writer yet."));
            }
            progress?.Report(new(FormatRestoreStage.Saving, mutations.Count, selected.Count));
            main.Document.Save();
        }
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new(FormatRestoreStage.Reparsing, 0, null));
        copy.Position = 0;
        var after = await parser.ParseAsync(copy, cancellationToken: cancellationToken);
        var bytes = copy.ToArray();
        progress?.Report(new(FormatRestoreStage.Validating, 0, null));
        ValidatePreservation(beforeBytes, bytes, before, after);
        var afterRuns = Paragraphs(after).SelectMany(p => p.Runs).ToDictionary(r => r.NodeId);
        foreach (var item in selected.Where(i => i.Eligibility == FormatRestoreEligibility.Eligible && i.Category == FormatRestoreCategory.Character))
            if (!afterRuns.TryGetValue(item.CurrentNodeId, out var run) || run.EffectiveFormatting != item.TargetFormatting.Character)
                throw new InvalidDataException("ReparseValidationFailed: target character formatting did not take effect.");
        var afterParagraphs = Paragraphs(after).ToDictionary(p => p.NodeId);
        foreach (var item in selected.Where(i => i.Eligibility == FormatRestoreEligibility.Eligible && i.Category == FormatRestoreCategory.Paragraph))
            if (!afterParagraphs.TryGetValue(item.CurrentNodeId, out var paragraph) || paragraph.EffectiveFormatting != item.TargetFormatting.Paragraph)
                throw new InvalidDataException("ReparseValidationFailed: target paragraph formatting did not take effect.");
        foreach (var item in selected.Where(i => i.Eligibility == FormatRestoreEligibility.Eligible && i.Category is FormatRestoreCategory.Table or FormatRestoreCategory.Cell))
        {
            RestoreFormatting? actual = item.NodeType switch
            {
                DocumentNodeKind.Table => after.Tables.Where(t => t.NodeId == item.CurrentNodeId).Select(t => new RestoreFormatting(Table: t.DirectFormatting)).SingleOrDefault(),
                DocumentNodeKind.Cell => after.Tables.SelectMany(t => t.Rows).SelectMany(r => r.Cells).Where(c => c.NodeId == item.CurrentNodeId).Select(c => new RestoreFormatting(Cell: c.DirectFormatting)).SingleOrDefault(),
                DocumentNodeKind.Row => after.Tables.SelectMany(t => t.Rows).Where(r => r.NodeId == item.CurrentNodeId).Select(r => new RestoreFormatting(RowHeightTwips: r.HeightTwips, RowHeightRule: r.HeightRule)).SingleOrDefault(),
                _ => null,
            };
            if (actual != item.TargetFormatting) throw new InvalidDataException("ReparseValidationFailed: target table/cell formatting did not take effect.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new(FormatRestoreStage.Completed, mutations.Count, selected.Count));
        return new(bytes, after, mutations, diagnostics);
    }

    internal static IReadOnlyList<FormatRestoreItem> SelectItems(FormatRestorePlan plan, FormatRestoreScope scope)
    {
        if (scope.Kind == FormatRestoreScopeKind.Category && scope.Category is null) throw new ArgumentException("Category is required.");
        if (scope.Kind == FormatRestoreScopeKind.SelectedItems &&
            (scope.SelectedItemIds is null || scope.SelectedItemIds.Any(id => plan.RestoreItems.All(i => i.RestoreItemId != id))))
            throw new ArgumentException("Unknown selected restore item.");
        return plan.RestoreItems.Where(i => scope.Kind switch
        {
            FormatRestoreScopeKind.All => true,
            FormatRestoreScopeKind.Category => i.Category == scope.Category || scope.Category == FormatRestoreCategory.Table && i.Category == FormatRestoreCategory.Cell,
            FormatRestoreScopeKind.SelectedItems => scope.SelectedItemIds!.Contains(i.RestoreItemId),
            _ => throw new ArgumentException("Unknown restore scope."),
        }).ToArray();
    }

    private static void ApplyCharacter(Run run, CharacterFormatSnapshot target, OpenXmlFormattingResolver resolver)
    {
        var paragraph = run.Ancestors<Paragraph>().First();
        var properties = run.RunProperties ??= new RunProperties();
        var original = resolver.ResolveRun(paragraph, run, null, "");
        var targetProperties = CharacterAttributes(target);
        var originalProperties = CharacterAttributes(original);
        foreach (var (elementName, attributes) in targetProperties)
        {
            if (Same(attributes, originalProperties[elementName])) continue;
            var changedAttributes = attributes.Keys.Where(name => attributes[name] != originalProperties[elementName][name]).ToArray();
            var element = properties.ChildElements.FirstOrDefault(e => e.LocalName == elementName && e.NamespaceUri == W);
            foreach (var name in changedAttributes) element?.RemoveAttribute(name, W);
            if (element is not null && !element.HasAttributes && !element.HasChildren) { element.Remove(); element = null; }
            var inherited = CharacterAttributes(resolver.ResolveRun(paragraph, run, null, ""))[elementName];
            var needed = changedAttributes.Where(name => attributes[name] != inherited[name]).ToArray();
            if (needed.Length == 0) continue;
            if (needed.Any(name => attributes[name] is null)) throw new InvalidDataException("UnrepresentableInheritedFormatting: target has no explicit value.");
            OpenXmlElement created = elementName switch
            {
                "rFonts" => new RunFonts(), "sz" => new FontSize(), "color" => new Color(),
                "b" => new Bold(), "i" => new Italic(), "u" => new Underline(),
                "strike" => new Strike(), "highlight" => new Highlight(),
                _ => throw new InvalidDataException("Unsupported character property."),
            };
            if (element is null) { element = created; properties.AddChild(element, true); }
            foreach (var name in needed) element.SetAttribute(new OpenXmlAttribute("w", name, W, attributes[name]!));
        }
        if (!properties.HasChildren && !properties.HasAttributes) run.RunProperties = null;
    }

    internal static Dictionary<string, Dictionary<string, string?>> CharacterAttributes(CharacterFormatSnapshot format) => new()
    {
        ["rFonts"] = new() { ["ascii"] = format.Fonts.Ascii, ["hAnsi"] = format.Fonts.HighAnsi, ["eastAsia"] = format.Fonts.EastAsia,
            ["cs"] = format.Fonts.ComplexScript, ["asciiTheme"] = format.Fonts.AsciiTheme, ["hAnsiTheme"] = format.Fonts.HighAnsiTheme,
            ["eastAsiaTheme"] = format.Fonts.EastAsiaTheme, ["cstheme"] = format.Fonts.ComplexScriptTheme },
        ["sz"] = new() { ["val"] = format.FontSizeHalfPoints?.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        ["color"] = new() { ["val"] = format.Color },
        ["b"] = new() { ["val"] = format.Bold is null ? null : format.Bold.Value ? "1" : "0" },
        ["i"] = new() { ["val"] = format.Italic is null ? null : format.Italic.Value ? "1" : "0" },
        ["u"] = new() { ["val"] = format.Underline },
        ["strike"] = new() { ["val"] = format.Strike is null ? null : format.Strike.Value ? "1" : "0" },
        ["highlight"] = new() { ["val"] = format.Highlight },
    };

    private static void ApplyParagraph(Paragraph paragraph, ParagraphFormatSnapshot target, string? targetStyle,
        Styles? styles, OpenXmlFormattingResolver resolver, List<FormatRestoreDiagnostic> diagnostics, string nodeId)
    {
        var properties = paragraph.ParagraphProperties ??= new ParagraphProperties();
        var oldStyle = properties.ParagraphStyleId?.Val?.Value;
        if (targetStyle != oldStyle)
        {
            var beforeRuns = paragraph.Descendants<Run>().Select(r => resolver.ResolveRun(paragraph, r, null, "")).ToArray();
            if (targetStyle is not null && styles?.Elements<Style>().Any(s => s.StyleId?.Value == targetStyle && s.Type?.Value == StyleValues.Paragraph) == true &&
                ExtraStyleSignature(styles, oldStyle) == ExtraStyleSignature(styles, targetStyle))
            {
                properties.ParagraphStyleId = new ParagraphStyleId { Val = targetStyle };
                if (!beforeRuns.SequenceEqual(paragraph.Descendants<Run>().Select(r => resolver.ResolveRun(paragraph, r, null, ""))))
                    properties.ParagraphStyleId = oldStyle is null ? null : new ParagraphStyleId { Val = oldStyle };
            }
            if (properties.ParagraphStyleId?.Val?.Value != targetStyle)
                diagnostics.Add(new("StyleReferencePreserved", nodeId, "Missing/incompatible style reference retained; only supported paragraph attributes are restored."));
        }
        var original = ParagraphAttributes(resolver.ResolveParagraph(paragraph, null, ""));
        foreach (var (elementName, attributes) in ParagraphAttributes(target))
        {
            var changed = attributes.Keys.Where(name => attributes[name] != original[elementName][name]).ToArray();
            if (changed.Length == 0) continue;
            var element = properties.ChildElements.FirstOrDefault(e => e.LocalName == elementName && e.NamespaceUri == W);
            foreach (var name in changed) element?.RemoveAttribute(name, W);
            if (element is not null && !element.HasAttributes && !element.HasChildren) { element.Remove(); element = null; }
            var inherited = ParagraphAttributes(resolver.ResolveParagraph(paragraph, null, ""))[elementName];
            var needed = changed.Where(name => attributes[name] != inherited[name]).ToArray();
            if (needed.Any(name => attributes[name] is null)) throw new InvalidDataException("UnrepresentableInheritedParagraphFormatting.");
            if (needed.Length == 0) continue;
            OpenXmlElement created = elementName switch
            {
                "jc" => new Justification(), "ind" => new Indentation(), "spacing" => new SpacingBetweenLines(),
                _ => throw new InvalidDataException("Unsupported paragraph property."),
            };
            if (element is null) { element = created; properties.AddChild(element, true); }
            foreach (var name in needed) element.SetAttribute(new("w", name, W, attributes[name]!));
        }
        if (!properties.HasChildren && !properties.HasAttributes) paragraph.ParagraphProperties = null;
    }

    internal static Dictionary<string, Dictionary<string, string?>> ParagraphAttributes(ParagraphFormatSnapshot format) => new()
    {
        ["jc"] = new() { ["val"] = format.Alignment },
        ["ind"] = new() { ["left"] = format.LeftIndent, ["right"] = format.RightIndent, ["firstLine"] = format.FirstLineIndent, ["hanging"] = format.HangingIndent },
        ["spacing"] = new() { ["before"] = format.SpacingBefore, ["after"] = format.SpacingAfter, ["line"] = format.LineSpacing, ["lineRule"] = format.LineRule },
    };

    private static void ApplyTableProperties(OpenXmlElement node, RestoreFormatting target, string kind)
    {
        var properties = node.ChildElements.FirstOrDefault(e => e.LocalName == kind && e.NamespaceUri == W);
        if (properties is null)
        {
            properties = kind switch { "tblPr" => new TableProperties(), "tcPr" => new TableCellProperties(), "trPr" => new TableRowProperties(), _ => throw new InvalidDataException() };
            ((OpenXmlCompositeElement)node).AddChild(properties, true);
        }
        foreach (var (name, attributes) in TableAttributes(target))
        {
            var existing = properties.ChildElements.FirstOrDefault(e => e.LocalName == name && e.NamespaceUri == W);
            foreach (var attribute in attributes.Keys) existing?.RemoveAttribute(attribute, W);
            if (existing is not null && !existing.HasChildren && !existing.HasAttributes) { existing.Remove(); existing = null; }
            if (attributes.All(a => a.Value is null)) continue;
            OpenXmlElement created = name switch
            {
                "tblW" => new TableWidth(), "jc" => new TableJustification(), "shd" => new Shading(), "tcW" => new TableCellWidth(),
                "vAlign" => new TableCellVerticalAlignment(), "trHeight" => new TableRowHeight(), _ => throw new InvalidDataException("Unsupported table property."),
            };
            if (existing is null) { existing = created; ((OpenXmlCompositeElement)properties).AddChild(existing, true); }
            foreach (var (attribute, value) in attributes.Where(a => a.Value is not null)) existing.SetAttribute(new("w", attribute, W, value!));
        }
        if (target.Table is { } table) ApplyBorders(properties, "tblBorders", table.Borders);
        if (target.Cell is { } cell) ApplyBorders(properties, "tcBorders", cell.Borders);
        if (!properties.HasChildren && !properties.HasAttributes) properties.Remove();
    }

    internal static Dictionary<string, Dictionary<string, string?>> TableAttributes(RestoreFormatting format)
    {
        if (format.Table is { } table) return new()
        {
            ["tblW"] = new() { ["w"] = table.Width, ["type"] = table.WidthType },
            ["jc"] = new() { ["val"] = table.Alignment }, ["shd"] = new() { ["fill"] = table.ShadingFill },
        };
        if (format.Cell is { } cell) return new()
        {
            ["tcW"] = new() { ["w"] = cell.Width, ["type"] = cell.WidthType },
            ["vAlign"] = new() { ["val"] = cell.VerticalAlignment }, ["shd"] = new() { ["fill"] = cell.ShadingFill },
        };
        return new() { ["trHeight"] = new() { ["val"] = format.RowHeightTwips?.ToString(System.Globalization.CultureInfo.InvariantCulture), ["hRule"] = format.RowHeightRule } };
    }

    private static void ApplyBorders(OpenXmlElement properties, string kind, TableBordersSnapshot target)
    {
        var borders = properties.ChildElements.FirstOrDefault(e => e.LocalName == kind && e.NamespaceUri == W);
        if (borders is null)
        {
            borders = kind == "tblBorders" ? new TableBorders() : new TableCellBorders();
            ((OpenXmlCompositeElement)properties).AddChild(borders, true);
        }
        foreach (var (name, border) in BorderValues(target))
        {
            var existing = borders.ChildElements.FirstOrDefault(e => e.LocalName == name && e.NamespaceUri == W);
            foreach (var attribute in new[] { "val", "color", "sz" }) existing?.RemoveAttribute(attribute, W);
            if (existing is not null && !existing.HasAttributes && !existing.HasChildren) { existing.Remove(); existing = null; }
            if (border is null) continue;
            OpenXmlElement created = name switch
            {
                "top" => new TopBorder(), "left" => new LeftBorder(), "bottom" => new BottomBorder(), "right" => new RightBorder(),
                "insideH" => new InsideHorizontalBorder(), "insideV" => new InsideVerticalBorder(), _ => throw new InvalidDataException(),
            };
            if (existing is null) { existing = created; ((OpenXmlCompositeElement)borders).AddChild(existing, true); }
            if (border.Style is not null) existing.SetAttribute(new("w", "val", W, border.Style));
            if (border.Color is not null) existing.SetAttribute(new("w", "color", W, border.Color));
            if (border.Size is not null) existing.SetAttribute(new("w", "sz", W, border.Size.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (!borders.HasChildren && !borders.HasAttributes) borders.Remove();
    }

    private static IEnumerable<(string Name, TableBorderSnapshot? Border)> BorderValues(TableBordersSnapshot borders) =>
        [("top", borders.Top), ("left", borders.Left), ("bottom", borders.Bottom), ("right", borders.Right), ("insideH", borders.InsideHorizontal), ("insideV", borders.InsideVertical)];

    private static string ExtraStyleSignature(Styles styles, string? styleId)
    {
        var index = styles.Elements<Style>().Where(s => s.StyleId?.Value is not null).ToDictionary(s => s.StyleId!.Value!);
        styleId ??= styles.Elements<Style>().FirstOrDefault(s => s.Type?.Value == StyleValues.Paragraph && s.Default?.Value == true)?.StyleId?.Value;
        var visited = new HashSet<string>();
        var extra = new List<string>();
        while (styleId is not null)
        {
            if (!visited.Add(styleId) || !index.TryGetValue(styleId, out var style)) return "invalid:" + styleId;
            if (style.StyleRunProperties is { } runs && runs.HasChildren) extra.Add(runs.OuterXml);
            if (style.StyleParagraphProperties is { } properties)
            {
                var clone = (StyleParagraphProperties)properties.CloneNode(true);
                StripProperties(clone, ParagraphAttributes(ParagraphFormatSnapshot.Empty));
                if (clone.HasChildren || clone.HasAttributes) extra.Add(clone.OuterXml);
            }
            styleId = style.BasedOn?.Val?.Value;
        }
        return string.Join("|", extra);
    }

    private static void StripProperties(OpenXmlElement properties, Dictionary<string, Dictionary<string, string?>> allowed)
    {
        foreach (var child in properties.ChildElements.Where(e => e.NamespaceUri == W && allowed.ContainsKey(e.LocalName)).ToArray())
        {
            foreach (var name in allowed[child.LocalName].Keys) child.RemoveAttribute(name, W);
            if (!child.HasChildren && !child.HasAttributes) child.Remove();
        }
    }

    internal static bool Same(Dictionary<string, string?> left, Dictionary<string, string?> right) =>
        left.Count == right.Count && left.All(a => right.TryGetValue(a.Key, out var value) && a.Value == value);

    internal static IEnumerable<DocumentParagraphSnapshot> Paragraphs(DocumentSnapshot snapshot) =>
        snapshot.Paragraphs.Concat(snapshot.Tables.SelectMany(t => t.Rows).SelectMany(r => r.Cells).SelectMany(c => c.Paragraphs));

    internal static void ValidatePreservation(byte[] beforeBytes, byte[] afterBytes, DocumentSnapshot before, DocumentSnapshot after)
    {
        if (JsonSerializer.Serialize(Paragraphs(before).Select(p => new { p.NodeId, p.RawText, p.DisplayText, Runs = p.Runs.Select(r => new { r.NodeId, r.RawText, r.DisplayText, r.Content }) })) !=
            JsonSerializer.Serialize(Paragraphs(after).Select(p => new { p.NodeId, p.RawText, p.DisplayText, Runs = p.Runs.Select(r => new { r.NodeId, r.RawText, r.DisplayText, r.Content }) })) ||
            JsonSerializer.Serialize(before.Revisions) != JsonSerializer.Serialize(after.Revisions) ||
            JsonSerializer.Serialize(before.Comments) != JsonSerializer.Serialize(after.Comments) || before.Tables.Count != after.Tables.Count)
            throw new InvalidDataException("ReparseValidationFailed: text, revision, comment or structure changed.");
        using var left = new ZipArchive(new MemoryStream(beforeBytes), ZipArchiveMode.Read);
        using var right = new ZipArchive(new MemoryStream(afterBytes), ZipArchiveMode.Read);
        if (!left.Entries.Select(e => e.FullName).Order().SequenceEqual(right.Entries.Select(e => e.FullName).Order()))
            throw new InvalidDataException("Package parts changed.");
        var mainPath = before.Metadata.MainDocumentPart.TrimStart('/');
        foreach (var entry in left.Entries)
        {
            using var a = entry.Open();
            using var b = right.GetEntry(entry.FullName)!.Open();
            using var ac = new MemoryStream(); a.CopyTo(ac);
            using var bc = new MemoryStream(); b.CopyTo(bc);
            if (entry.FullName == mainPath)
            {
                if (ProtectedXml(ac.ToArray()) != ProtectedXml(bc.ToArray())) throw new InvalidDataException("Protected main-document XML changed.");
            }
            else if (!ac.ToArray().AsSpan().SequenceEqual(bc.ToArray())) throw new InvalidDataException("An unhandled package part changed.");
        }
    }

    private static string ProtectedXml(byte[] bytes)
    {
        var document = XDocument.Load(new MemoryStream(bytes));
        XNamespace w = W;
        var allowed = CharacterAttributes(CharacterFormatSnapshot.Empty);
        foreach (var properties in document.Descendants(w + "rPr").Where(p => !p.Ancestors().Any(a => a.Name.LocalName.EndsWith("Change", StringComparison.Ordinal))))
            foreach (var element in properties.Elements().Where(e => e.Name.Namespace == w && allowed.ContainsKey(e.Name.LocalName)).ToArray())
            {
                foreach (var attribute in element.Attributes().Where(a => a.Name.Namespace == w && allowed[element.Name.LocalName].ContainsKey(a.Name.LocalName)).ToArray()) attribute.Remove();
                if (!element.HasElements && !element.Attributes().Any(a => !a.IsNamespaceDeclaration)) element.Remove();
            }
        document.Descendants(w + "rPr").Where(e => !e.HasElements && !e.Attributes().Any(a => !a.IsNamespaceDeclaration)).Remove();
        foreach (var properties in document.Descendants(w + "pPr").Where(p => !p.Ancestors().Any(a => a.Name.LocalName.EndsWith("Change", StringComparison.Ordinal))))
        {
            var paragraphAllowed = ParagraphAttributes(ParagraphFormatSnapshot.Empty);
            paragraphAllowed.Add("pStyle", new() { ["val"] = null });
            foreach (var element in properties.Elements().Where(e => e.Name.Namespace == w && paragraphAllowed.ContainsKey(e.Name.LocalName)).ToArray())
            {
                foreach (var attribute in element.Attributes().Where(a => a.Name.Namespace == w && paragraphAllowed[element.Name.LocalName].ContainsKey(a.Name.LocalName)).ToArray()) attribute.Remove();
                if (!element.HasElements && !element.Attributes().Any(a => !a.IsNamespaceDeclaration)) element.Remove();
            }
        }
        document.Descendants(w + "pPr").Where(e => !e.HasElements && !e.Attributes().Any(a => !a.IsNamespaceDeclaration)).Remove();
        foreach (var kind in new[] { "tblPr", "tcPr", "trPr" })
        {
            var tableAllowed = kind == "tblPr" ? TableAttributes(new(Table: TableFormatSnapshot.Empty)) :
                kind == "tcPr" ? TableAttributes(new(Cell: TableCellFormatSnapshot.Empty)) : TableAttributes(new());
            foreach (var properties in document.Descendants(w + kind).Where(p => !p.Ancestors().Any(a => a.Name.LocalName.EndsWith("Change", StringComparison.Ordinal))))
            {
                foreach (var element in properties.Elements().Where(e => e.Name.Namespace == w && tableAllowed.ContainsKey(e.Name.LocalName)).ToArray())
                {
                    foreach (var attribute in element.Attributes().Where(a => a.Name.Namespace == w && tableAllowed[element.Name.LocalName].ContainsKey(a.Name.LocalName)).ToArray()) attribute.Remove();
                    if (!element.HasElements && !element.Attributes().Any(a => !a.IsNamespaceDeclaration)) element.Remove();
                }
                foreach (var borders in properties.Elements().Where(e => e.Name == w + "tblBorders" || e.Name == w + "tcBorders").ToArray())
                {
                    foreach (var border in borders.Elements().Where(e => new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Contains(e.Name.LocalName)).ToArray())
                    {
                        border.Attributes().Where(a => a.Name.Namespace == w && new[] { "val", "color", "sz" }.Contains(a.Name.LocalName)).Remove();
                        if (!border.HasElements && !border.Attributes().Any(a => !a.IsNamespaceDeclaration)) border.Remove();
                    }
                    if (!borders.HasElements && !borders.Attributes().Any(a => !a.IsNamespaceDeclaration)) borders.Remove();
                }
            }
            document.Descendants(w + kind).Where(e => !e.HasElements && !e.Attributes().Any(a => !a.IsNamespaceDeclaration)).Remove();
        }
        // SDK property constructors can add redundant xmlns declarations. Compare expanded
        // names and values rather than lexical prefixes without ignoring protected content.
        return JsonSerializer.Serialize(SemanticXml(document.Root!));
    }

    private static object SemanticXml(XElement element) => new
    {
        Name = element.Name.ToString(),
        Attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration).OrderBy(a => a.Name.ToString(), StringComparer.Ordinal)
            .Select(a => new { Name = a.Name.ToString(), a.Value }).ToArray(),
        Nodes = element.Nodes().Select(n => n is XElement child ? SemanticXml(child) : (object)new { Kind = n.NodeType.ToString(), Value = n.ToString(SaveOptions.DisableFormatting) }).ToArray(),
    };
}
