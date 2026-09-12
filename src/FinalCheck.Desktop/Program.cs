using Avalonia;
using FinalCheck.App.ViewModels;
using FinalCheck.Comparison;
using FinalCheck.Core.Abstractions;
using FinalCheck.Data;
using FinalCheck.Documents;
using FinalCheck.Infrastructure;
using Microsoft.EntityFrameworkCore;
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
        ConfigureServices(services, args);
        var serviceProvider = services.BuildServiceProvider();

        using (var scope = serviceProvider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<FinalCheckDatabaseInitializer>()
                .InitializeAsync()
                .GetAwaiter()
                .GetResult();
        }

        FinalCheckApplication.ConfigureServices(serviceProvider);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<FinalCheckApplication>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void ConfigureServices(IServiceCollection services, string[] args)
    {
        services.AddSingleton<IAppDataPathProvider, PlatformAppDataPathProvider>();
#if DEBUG
        var developerPathIndex = Array.IndexOf(args, "--developer-data-directory");
        if (developerPathIndex >= 0)
        {
            if (developerPathIndex + 1 >= args.Length) throw new ArgumentException("An absolute isolated developer data directory is required.");
            services.AddSingleton<IAppDataPathProvider>(new DeveloperAppDataPathProvider(args[developerPathIndex + 1]));
        }
#endif
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

        services.AddDbContext<FinalCheckDbContext>((serviceProvider, options) =>
        {
            var databasePath = serviceProvider.GetRequiredService<IAppDataPathProvider>().GetDatabasePath();
            options.UseSqlite($"Data Source={databasePath}");
        });
        services.AddScoped<FinalCheckDatabaseInitializer>();
        services.AddScoped<IComparisonResultStore, ComparisonResultStore>();
        services.AddScoped<IFormatRestoreStore, FormatRestoreStore>();
        services.AddScoped<IFormatRestoreWorkingCopyService, FormatRestoreWorkingCopyService>();
    }
}
