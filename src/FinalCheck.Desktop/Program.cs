using Avalonia;
using FinalCheck.App.ViewModels;
using FinalCheck.Comparison;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Storage;
using FinalCheck.Data;
using FinalCheck.Documents;
using FinalCheck.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Velopack;
using FinalCheckApplication = FinalCheck.App.App;

namespace FinalCheck.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack lifecycle hooks must run before Avalonia, DI, database access, or other app code.
        VelopackApp.Build().Run();

        var services = new ServiceCollection();
        var platformPaths = GetPlatformPaths(args);
        try { ConfigureServices(services, platformPaths); }
        catch (Exception error)
        {
            // Minimal startup-control diagnostic remains locatable even when DataRoot is unavailable.
            StorageStartupDiagnostics.Write(platformPaths, "BootstrapResolution", error);
            throw;
        }
        var serviceProvider = services.BuildServiceProvider();

        using (var scope = serviceProvider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<FinalCheckDatabaseInitializer>()
                .InitializeAsync()
                .GetAwaiter()
                .GetResult();
            serviceProvider.GetRequiredService<DataRootBootstrapResolver>().MarkDatabaseInitializedAsync().GetAwaiter().GetResult();
        }

        FinalCheckApplication.ConfigureServices(serviceProvider);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<FinalCheckApplication>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859", Justification = "Platform abstraction also has a Debug-only isolated implementation.")]
    private static IPlatformStoragePaths GetPlatformPaths(string[] args)
    {
#if DEBUG
        var developerPathIndex = Array.IndexOf(args, "--developer-data-directory");
        if (developerPathIndex >= 0)
        {
            if (developerPathIndex + 1 >= args.Length) throw new ArgumentException("An absolute isolated developer data directory is required.");
            return new DeveloperStoragePaths(args[developerPathIndex + 1]);
        }
#endif
        return new PlatformStoragePaths();
    }

    private static void ConfigureServices(IServiceCollection services, IPlatformStoragePaths platformPaths)
    {
        var bootstrap = new FileStorageBootstrapStore(platformPaths);
        var inspector = new SqliteDataRootDatabaseInspector();
        var resolver = new DataRootBootstrapResolver(platformPaths, bootstrap, inspector);
        var resolved = resolver.ResolveAsync().GetAwaiter().GetResult();
        services.AddSingleton(platformPaths);
        services.AddSingleton<IStorageBootstrapStore>(bootstrap);
        services.AddSingleton<IDataRootDatabaseInspector>(inspector);
        services.AddSingleton(resolver);
        services.AddSingleton<IDataRootProvider>(resolved.Provider);
        services.AddSingleton<IAppDataPathProvider, PlatformAppDataPathProvider>();
        services.AddSingleton<IFileHashService, Sha256FileHashService>();
        services.AddSingleton<IUpdateService, DeferredUpdateService>();
        services.AddSingleton<IDocumentParser, OpenXmlDocumentParser>();
        services.AddSingleton<IDocumentSnapshotSerializer, JsonDocumentSnapshotSerializer>();
        services.AddSingleton<IDocumentPreviewRenderer, SnapshotHtmlPreviewRenderer>();
        services.AddSingleton<IStructureMatcher, MultiSignalParagraphMatcher>();
        services.AddSingleton<ITextTokenizer, MixedLanguageTextTokenizer>();
        services.AddSingleton<ITextDiffService, TokenTextDiffService>();
        services.AddSingleton<IParagraphMoveDetector, ParagraphMoveDetector>();
        services.AddSingleton<IFormatDiffService, EffectiveFormatDiffService>();
        services.AddSingleton<ITableComparisonService, TableComparisonService>();
        services.AddSingleton<IAnnotationIntegrationService, SnapshotAnnotationIntegrationService>();
        services.AddSingleton<IChangeGroupingService, RuleBasedChangeGroupingService>();
        services.AddSingleton<IComparisonResultSerializer, JsonComparisonResultSerializer>();
        services.AddSingleton<IComparisonEngine, BasicComparisonEngine>();
        services.AddSingleton<IFormatRestorePlanner, SnapshotFormatRestorePlanner>();
        services.AddSingleton<IFormatRestoreRenderer, OpenXmlFormatRestoreRenderer>();
        services.AddSingleton<IWorkingCopyComparisonService, WorkingCopyComparisonService>();
        services.AddSingleton<MainViewModel>();

        services.AddSingleton<DataRootDbContextFactory>();
        services.AddScoped(sp => sp.GetRequiredService<DataRootDbContextFactory>().CreateDbContext());
        services.AddScoped<FinalCheckDatabaseInitializer>();
        services.AddScoped<DocumentSnapshotStore>();
        services.AddScoped<IComparisonResultStore, ComparisonResultStore>();
        services.AddScoped<IFormatRestoreStore, FormatRestoreStore>();
        services.AddScoped<IFormatRestoreWorkingCopyService, FormatRestoreWorkingCopyService>();
    }
}
