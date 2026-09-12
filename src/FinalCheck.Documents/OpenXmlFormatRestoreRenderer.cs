using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Documents;
using FinalCheck.Core.Formatting;

namespace FinalCheck.Documents;

public sealed class OpenXmlFormatRestoreRenderer(IDocumentParser parser) : IFormatRestoreRenderer
{
    internal const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public async ValueTask<FormatRestoreRenderResult> RenderAsync(Stream source, FormatRestorePlan plan,
        FormatRestoreScope? scope = null, IProgress<FormatRestoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);
        if (!source.CanRead || !source.CanSeek) throw new ArgumentException("A readable seekable source is required.");
        if (plan.SchemaVersion != FormatRestorePlan.CurrentSchemaVersion) throw new InvalidDataException("Unsupported restore plan schema.");
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
        document.Descendants(w + "rPr").Where(e => !e.HasElements && !e.HasAttributes).Remove();
        return document.ToString(SaveOptions.DisableFormatting);
    }
}
