using Avalonia;
using FinalCheck.App.ViewModels;
using FinalCheck.Comparison;
using FinalCheck.Core.Abstractions;
using FinalCheck.Core.Storage;
using FinalCheck.Core.Comparisons;
using FinalCheck.Core.Management;
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
        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        try { DesktopStorageServices.InitializeAsync(serviceProvider).GetAwaiter().GetResult(); ProjectLifecycleService.RecoverAsync(serviceProvider).GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            StorageStartupDiagnostics.Write(platformPaths, "StorageRecoveryInitialization", error);
            throw;
        }
#if DEBUG
        if (StorageDeveloperCommands.RunAsync(args, platformPaths, serviceProvider).GetAwaiter().GetResult()) return;
#endif

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
        return new PlatformStoragePaths(Velopack.Locators.VelopackLocator.Current.RootAppDir ?? AppContext.BaseDirectory);
    }

    private static void ConfigureServices(IServiceCollection services, IPlatformStoragePaths platformPaths)
    {
        IStorageVolumeInfoProvider? volumes = null;
#if DEBUG
        if (platformPaths is DeveloperStoragePaths developer) volumes = new DeveloperStorageVolumeInfo(developer);
#endif
        DesktopStorageServices.ConfigureAsync(services, platformPaths, volumes).GetAwaiter().GetResult();
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
        services.AddSingleton<IComparisonFileInspector, ComparisonFileInspector>();
        services.AddSingleton<IComparisonWorkflowService, ComparisonWorkflowService>();
        services.AddSingleton<ComparisonSetupViewModel>();
        services.AddSingleton<ITemplateService, TemplateService>();
        services.AddSingleton<TemplateCenterViewModel>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IContractVersionService, ContractVersionService>();
        services.AddSingleton<IProjectComparisonService, ProjectComparisonService>();
        services.AddSingleton<IProjectLifecycleService, ProjectLifecycleService>();
        services.AddSingleton<VersionManagementViewModel>();
        services.AddSingleton<ProjectsViewModel>();
        services.AddSingleton<MainViewModel>();

        services.AddScoped<DocumentSnapshotStore>();
        services.AddScoped<ITemplateStore, TemplateStore>();
        services.AddScoped<IProjectStore, ProjectStore>();
        services.AddScoped<IContractVersionStore, ContractVersionStore>();
        services.AddScoped<IProjectComparisonStore, ProjectComparisonStore>();
        services.AddScoped<IProjectLifecycleStore, ProjectLifecycleStore>();
        services.AddScoped<IComparisonResultStore, ComparisonResultStore>();
        services.AddScoped<IComparisonRecordStore, ComparisonRecordStore>();
        services.AddScoped<IFormatRestoreStore, FormatRestoreStore>();
        services.AddScoped<IFormatRestoreWorkingCopyService, FormatRestoreWorkingCopyService>();
    }
}
