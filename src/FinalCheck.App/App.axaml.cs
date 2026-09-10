using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FinalCheck.App.ViewModels;
using FinalCheck.App.Views;

namespace FinalCheck.App;

public partial class App : Application
{
    private static IServiceProvider? services;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        services = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = services?.GetService(typeof(MainViewModel)) as MainViewModel
                    ?? new MainViewModel(),
            };
            desktop.Exit += (_, _) => (services as IDisposable)?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
