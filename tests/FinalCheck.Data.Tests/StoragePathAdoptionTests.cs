using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalCheck.Comparison;
using FinalCheck.Core.Storage;
using FinalCheck.Core.Formatting;
using FinalCheck.Documents;
using FinalCheck.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinalCheck.Data.Tests;

public sealed class StoragePathAdoptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultAndCustomRootPersistSnapshotComparisonAndRestoreWithoutMovingOriginal(bool custom)
    {
        using var fixture = new StorageFixture();
        var initial = await fixture.Resolver.ResolveAsync();
        IDataRootProvider paths = initial.Provider;
        if (custom)
        {
            var descriptor = fixture.Descriptor(Path.Combine(fixture.DirectoryPath, "中文 custom;Data")) with { Generation = 2 };
            DataRootBootstrapResolver.WriteIdentity(descriptor);
            fixture.Bootstrap.Save(new(1, descriptor, descriptor, false), initial.Provider.Descriptor.Generation);
            paths = (await fixture.Resolver.ResolveAsync()).Provider;
        }
        var factory = new DataRootDbContextFactory(paths);
        await using var context = factory.CreateDbContext();
        await new FinalCheckDatabaseInitializer(context).InitializeAsync();
        await fixture.Resolver.MarkDatabaseInitializedAsync();
        var originals = Path.Combine(fixture.DirectoryPath, "user-originals");
        Directory.CreateDirectory(originals);
        var original = Path.Combine(originals, "current.docx");
        var baseline = Path.Combine(originals, "baseline.docx");
        WriteDocument(original, false);
        WriteDocument(baseline, true);
        var before = File.ReadAllBytes(original);
        var parser = new OpenXmlDocumentParser();
        var current = await parser.ParseFileAsync(original);
        var template = await parser.ParseFileAsync(baseline);
        var engine = CreateEngine();
        var comparison = engine.Compare(template, current);
        var snapshots = new DocumentSnapshotStore(context, new JsonDocumentSnapshotSerializer());
        var comparisons = new ComparisonResultStore(context, new JsonComparisonResultSerializer());
        var snapshotId = await snapshots.SaveAsync(current);
        var comparisonId = await comparisons.SaveAsync(comparison);
        var store = new FormatRestoreStore(context, new JsonDocumentSnapshotSerializer(), new JsonComparisonResultSerializer());
        var service = new FormatRestoreWorkingCopyService(new PlatformAppDataPathProvider(paths), parser,
            new OpenXmlFormatRestoreRenderer(parser), store, new WorkingCopyComparisonService(engine));
        var version = Guid.NewGuid();
        var plan = new SnapshotFormatRestorePlanner().Generate(template, current, comparison);
        var restored = await service.ExecuteAsync(version, original, plan);
        Assert.Equal(FormatRestoreResultStatus.Completed, restored.Status);
        Assert.StartsWith(paths.WorkingCopyPath + Path.DirectorySeparatorChar, restored.WorkingCopy!.WorkingPath);
        Assert.Equal(original, restored.WorkingCopy.OriginalPath);
        Assert.Equal(before, File.ReadAllBytes(original));
        Assert.Equal("第1条 双方约定付款期限60日，按照合同约定履行义务。", restored.WorkingCopy.Snapshot.Paragraphs[0].DisplayText);
        Assert.True(restored.WorkingCopy.Snapshot.Paragraphs[0].Runs[0].EffectiveFormatting.Bold);
        Assert.Equal(current.Metadata.Sha256, (await snapshots.LoadAsync(snapshotId))!.Metadata.Sha256);
        Assert.NotNull(await comparisons.LoadAsync(comparisonId));
        await using var reopened = factory.CreateDbContext();
        Assert.True(await reopened.DocumentSnapshots.AnyAsync(s => s.Id == snapshotId));
        Assert.True(await reopened.ComparisonResults.AnyAsync(c => c.Id == comparisonId));
        Assert.Single(await reopened.RestoredWorkingCopies.ToArrayAsync());
        Assert.False(Directory.Exists(fixture.Paths.InstallDirectory));
        Assert.False(File.Exists(Path.Combine(paths.CurrentDataRoot, "current.docx")));
    }

    [Fact]
    public async Task StartupDiagnosticsStayLocatableWhenDataRootCannotBeOpened()
    {
        using var fixture = new StorageFixture();
        StorageStartupDiagnostics.Write(fixture.Paths, "BootstrapResolution", new DirectoryNotFoundException("sensitive-path"));
        var diagnostic = File.ReadAllText(Path.Combine(fixture.Paths.ConfigurationDirectory, "startup-diagnostic.log"));
        Assert.Contains("DirectoryNotFoundException", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-path", diagnostic, StringComparison.Ordinal);
        // A prior control log must not turn a genuine new user into damaged legacy business data.
        Assert.Equal("NewUser", (await fixture.Resolver.ResolveAsync()).RecoveryCode);
    }

    [Fact]
    public void DesignTimeFactoryUsesAnExplicitIsolatedProviderRoot()
    {
        using var fixture = new StorageFixture();
        var root = Path.Combine(fixture.DirectoryPath, "design-only");
        using var context = new FinalCheckDbContextFactory().CreateDbContext(["--data-root", root]);
        Assert.Equal(Path.Combine(root, "finalcheck.db"), context.Database.GetDbConnection().DataSource);
        Assert.False(File.Exists(fixture.Bootstrap.BootstrapPath));
        Assert.False(File.Exists(Path.Combine(root, "finalcheck.db")));
    }

    internal static BasicComparisonEngine CreateEngine()
    {
        var diff = new TokenTextDiffService(new MixedLanguageTextTokenizer());
        var format = new EffectiveFormatDiffService();
        return new(new MultiSignalParagraphMatcher(), diff, new ParagraphMoveDetector(), format,
            new TableComparisonService(diff, format), new SnapshotAnnotationIntegrationService(), new RuleBasedChangeGroupingService());
    }
    internal static void WriteDocument(string path, bool formatted)
    {
        using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        main.Document = new(new Body(new Paragraph(new ParagraphProperties(), new Run(
            formatted ? new RunProperties(new Bold()) : new RunProperties(),
            new Text("第1条 双方约定付款期限60日，按照合同约定履行义务。")))));
        main.Document.Save();
    }
}
