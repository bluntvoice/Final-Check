using Avalonia;
using FinalCheck.App.ViewModels;
using FinalCheck.Comparison;
using FinalCheck.Core.Abstractions;
using FinalCheck.Data;
using FinalCheck.Documents;
using FinalCheck.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FinalCheckApplication = FinalCheck.App.App;

namespace FinalCheck.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
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

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAppDataPathProvider, PlatformAppDataPathProvider>();
        services.AddSingleton<IFileHashService, Sha256FileHashService>();
        services.AddSingleton<IUpdateService, DeferredUpdateService>();
        services.AddSingleton<IDocumentParser, OpenXmlDocumentParser>();
        services.AddSingleton<IDocumentSnapshotSerializer, JsonDocumentSnapshotSerializer>();
        services.AddSingleton<IDocumentPreviewRenderer, SnapshotHtmlPreviewRenderer>();
        services.AddSingleton<IStructureMatcher, MultiSignalParagraphMatcher>();
        services.AddSingleton<ITextTokenizer, MixedLanguageTextTokenizer>();
        services.AddSingleton<ITextDiffService, TokenTextDiffService>();
        services.AddSingleton<IComparisonEngine, BasicComparisonEngine>();
        services.AddSingleton<MainViewModel>();

        services.AddDbContext<FinalCheckDbContext>((serviceProvider, options) =>
        {
            var databasePath = serviceProvider.GetRequiredService<IAppDataPathProvider>().GetDatabasePath();
            options.UseSqlite($"Data Source={databasePath}");
        });
        services.AddScoped<FinalCheckDatabaseInitializer>();
    }
}
