using System.Windows;
using SiteManager.App.ViewModels;
using SiteManager.App.Views;
using SiteManager.Core.Configuration;
using SiteManager.Infrastructure.Configuration;
using SiteManager.Infrastructure.Ssh;

namespace SiteManager.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        IServerProfileStore settingsStore = new JsonSettingsStore(JsonSettingsStore.GetDefaultPath());
        ServerProfile? profile;
        try
        {
            profile = await settingsStore.LoadAsync(CancellationToken.None);
        }
        catch
        {
            profile = null;
        }

        var pages = await AppPageComposition.CreateAsync(settingsStore, new SshNetRemotePublisherFactory(), profile);
        var shell = new ShellViewModel(pages);
        var window = new MainWindow(shell);
        MainWindow = window;
        window.Show();
        if (pages.IsServerConfigured)
        {
            _ = shell.LiveSites.StartInitialSyncAsync();
        }
    }
}
