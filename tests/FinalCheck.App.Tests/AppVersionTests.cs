using System.Reflection;
using FinalCheck.App.ViewModels;

namespace FinalCheck.App.Tests;

public sealed class AppVersionTests
{
    [Fact]
    public void VersionLabelUsesTheAssemblyInformationalVersion()
    {
        var expected = typeof(MainViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var viewModel = new MainViewModel();

        Assert.Equal(expected, viewModel.AppVersion);
        Assert.Equal($"v{expected}", viewModel.AppVersionLabel);
        Assert.NotEqual("unknown", viewModel.AppVersion);
    }

    [Fact]
    public void AboutNavigationDisplaysTheSameVersionAsTheSidebar()
    {
        var viewModel = new MainViewModel();

        viewModel.NavigateCommand.Execute("about");

        Assert.Equal("关于 Final Check", viewModel.CurrentPageTitle);
        Assert.Contains(viewModel.AppVersionLabel, viewModel.CurrentPageDescription, StringComparison.Ordinal);
    }
}
