using Xunit;
using SiteManager.App.ViewModels;
using SiteManager.App.Tests.ViewModels;
using SiteManager.Core.Configuration;
using SiteManager.Core.Models;
using SiteManager.Core.Publishing;
using SiteManager.Core.Storage;
using SiteManager.Core.Transfers;

namespace SiteManager.App.Tests.Composition;

public sealed class StartupCompositionTests
{
    [Fact]
    public void Startup_awaits_settings_and_page_composition_instead_of_blocking_ui_thread()
    {
        var repositoryRoot = FindRepositoryRoot();
        var startup = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SiteManager.App", "App.xaml.cs"));
        var composition = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SiteManager.App", "ViewModels", "AppPageComposition.cs"));

        Assert.Contains("async void OnStartup", startup, StringComparison.Ordinal);
        Assert.Contains("await settingsStore.LoadAsync", startup, StringComparison.Ordinal);
        Assert.Contains("await AppPageComposition.CreateAsync", startup, StringComparison.Ordinal);
        Assert.Contains("Task<AppPageModels> CreateAsync", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadAsync(CancellationToken.None).GetAwaiter().GetResult()", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()", composition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configured_composition_loads_cached_sites_before_remote_sync()
    {
        var cachedSite = new SiteManifest(Guid.NewGuid(), "cached", string.Empty, "cached-site", SiteStatus.Live, 1, 0,
            new string('a', 64), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);
        var cache = new CompositionCache([cachedSite]);
        var profile = new ServerProfile("example.test", 22, "sitepublisher", "key", "SHA256:ZrZ2SF13RvyeSsLMuHl27GIelk8Yb09f1PBBae/1tbU", "http://example.test/s/");

        var pages = await AppPageComposition.CreateAsync(
            new CompositionSettingsStore(),
            new FakeRemotePublisherFactory(new FakeRemotePublisher()),
            profile,
            TestContext.Current.CancellationToken,
            cache);

        var loaded = Assert.Single(pages.LiveSites.Sites);
        Assert.Equal(cachedSite, loaded);
        Assert.True(cache.Initialized);
    }

    [Fact]
    public void Startup_starts_initial_sync_after_showing_the_window()
    {
        var repositoryRoot = FindRepositoryRoot();
        var startup = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SiteManager.App", "App.xaml.cs"));

        Assert.Contains("window.Show()", startup, StringComparison.Ordinal);
        Assert.Contains("StartInitialSyncAsync", startup, StringComparison.Ordinal);
    }

    [Fact]
    public void Transfer_progress_bar_is_one_way_for_read_only_progress()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SiteManager.App", "Views", "TransferCenterView.xaml"));

        Assert.Contains("Value=\"{Binding ProgressPercent, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_binds_server_status_instead_of_static_placeholder_text()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SiteManager.App", "Views", "MainWindow.xaml"));

        Assert.Contains("Text=\"{Binding ServerStatusTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ServerStatusDescription}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("设置页面将在下一阶段接入", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_error_banner_binds_a_boolean_visibility_property()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SiteManager.App", "Views", "PublishView.xaml"));

        Assert.Contains("Visibility=\"{Binding HasError, Converter={StaticResource BooleanToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_view_does_not_expose_a_secondary_new_site_action()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SiteManager.App", "Views", "PublishView.xaml"));

        Assert.DoesNotContain("Content=\"新建网站\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding NewSiteCommand}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_labels_the_primary_action_as_new_site_publish()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SiteManager.App", "Views", "MainWindow.xaml"));

        Assert.Contains("Content=\"上架新网页\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Transfer_center_binds_persisted_history_entries()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SiteManager.App", "Views", "TransferCenterView.xaml"));

        Assert.Contains("Text=\"传输历史\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding History}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_start_is_wired_to_transfer_center_navigation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var shell = File.ReadAllText(Path.Combine(repositoryRoot, "src", "SiteManager.App", "ViewModels", "ShellViewModel.cs"));

        Assert.Contains("Publish.TransferRequested += ShowTransfers", shell, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SiteManager.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class CompositionSettingsStore : IServerProfileStore
    {
        public Task<ServerProfile?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<ServerProfile?>(null);

        public Task SaveAsync(ServerProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CompositionCache(IReadOnlyList<SiteManifest> sites) : ISiteCache
    {
        public bool Initialized { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            Initialized = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SiteManifest>> GetSitesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(sites);

        public Task ReplaceSitesAsync(IReadOnlyCollection<SiteManifest> sites, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveCheckpointAsync(TransferCheckpoint checkpoint, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TransferCheckpoint?> GetCheckpointAsync(Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult<TransferCheckpoint?>(null);

        public Task DeleteCheckpointAsync(Guid requestId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
