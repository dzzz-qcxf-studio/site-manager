using System.IO;
using SiteManager.Core.Configuration;
using SiteManager.Core.Models;
using SiteManager.Core.Publishing;
using SiteManager.Core.Storage;
using SiteManager.Core.Transfers;
using SiteManager.Core.Validation;
using SiteManager.Infrastructure.Archives;
using SiteManager.Infrastructure.Configuration;
using SiteManager.Infrastructure.Ssh;
using SiteManager.Infrastructure.Storage;

namespace SiteManager.App.ViewModels;

public sealed record AppPageModels(
    LiveSitesViewModel LiveSites,
    PublishViewModel Publish,
    TransferCenterViewModel Transfers,
    TrashViewModel Trash,
    SettingsViewModel Settings,
    bool IsServerConfigured,
    string? ServerHost);

public static class AppPageComposition
{
    public static AppPageModels Create() => Create(
        new MemorySettingsStore(),
        new SshNetRemotePublisherFactory(),
        profile: null);

    public static AppPageModels Create(
        IServerProfileStore settingsStore,
        IRemotePublisherFactory remotePublisherFactory,
        ServerProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(remotePublisherFactory);

        if (profile is not null)
        {
            throw new InvalidOperationException("Configured pages must be created with CreateAsync so the local cache can initialize without blocking the UI thread.");
        }

        var settings = new SettingsViewModel(settingsStore, new RemoteConnectionTester(remotePublisherFactory), CreateDefaultProfile());
        return CreateUnavailablePages(settings);
    }

    public static async Task<AppPageModels> CreateAsync(
        IServerProfileStore settingsStore,
        IRemotePublisherFactory remotePublisherFactory,
        ServerProfile? profile,
        CancellationToken cancellationToken = default,
        ISiteCache? cacheOverride = null)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(remotePublisherFactory);

        var connectionTester = new RemoteConnectionTester(remotePublisherFactory);
        var settings = new SettingsViewModel(settingsStore, connectionTester, profile ?? CreateDefaultProfile());
        if (profile is null)
        {
            return CreateUnavailablePages(settings);
        }

        var remotePublisher = remotePublisherFactory.Create(profile);
        var cache = cacheOverride ?? CreateCache();
        await cache.InitializeAsync(cancellationToken);
        var folderPathStore = new JsonSiteFolderPathStore(JsonSiteFolderPathStore.GetDefaultPath());
        try
        {
            await folderPathStore.InitializeAsync(cancellationToken);
        }
        catch
        {
            // Folder history is auxiliary UI state; a corrupt or locked file
            // must not prevent the application from opening.
        }
        var transferHistoryStore = new JsonTransferHistoryStore(JsonTransferHistoryStore.GetDefaultPath());
        var pages = CreateConfiguredPages(settings, profile, remotePublisher, cache, folderPathStore, transferHistoryStore);
        try
        {
            await pages.Transfers.InitializeAsync(cancellationToken);
        }
        catch
        {
            // Transfer history is auxiliary UI state; the current task can
            // still be displayed and future entries can be written normally.
        }
        try
        {
            pages.LiveSites.ReplaceSites(await cache.GetSitesAsync(cancellationToken));
        }
        catch
        {
            // A stale/corrupt local cache must not prevent the application from
            // opening; the post-show sync can repopulate it from the server.
        }

        return pages;
    }

    private static AppPageModels CreateUnavailablePages(SettingsViewModel settings)
    {
        var transferCenter = new TransferCenterViewModel();
        var unavailableRemote = new UnavailableRemotePublisher();
        var unavailableSync = new UnavailableSiteSyncService();
        var catalog = new SiteCatalogState();
        return new AppPageModels(
            new LiveSitesViewModel(unavailableSync, unavailableRemote, new WpfClipboardService(), new SystemBrowserService(), new DefaultSiteLinkService(), catalog),
            new PublishViewModel(new WebsiteFolderValidator(), new UnavailablePublishSiteService(), transferCenter, new DefaultArchivePathFactory()),
            transferCenter,
            new TrashViewModel(unavailableSync, unavailableRemote, new WpfConfirmationService(), catalog),
            settings,
            IsServerConfigured: false,
            ServerHost: null);
    }

    private static AppPageModels CreateConfiguredPages(
        SettingsViewModel settings,
        ServerProfile profile,
        IRemotePublisher remotePublisher,
        ISiteCache cache,
        ISiteFolderPathStore folderPathStore,
        ITransferHistoryStore transferHistoryStore)
    {
        var transferCenter = new TransferCenterViewModel(transferHistoryStore);
        var catalog = new SiteCatalogState();
        var siteSync = new SiteSyncService(remotePublisher, cache);
        var publish = new PublishSiteService(
            new WebsiteFolderValidator(),
            new TarGzipArchiveBuilder(),
            remotePublisher,
            cache,
            new ResumableUploadEngine());
        return new AppPageModels(
            new LiveSitesViewModel(siteSync, remotePublisher, new WpfClipboardService(), new SystemBrowserService(), new DefaultSiteLinkService(profile.PublicBaseUrl), catalog),
            new PublishViewModel(new WebsiteFolderValidator(), publish, transferCenter, new DefaultArchivePathFactory(), folderPathStore),
            transferCenter,
            new TrashViewModel(siteSync, remotePublisher, new WpfConfirmationService(), catalog),
            settings,
            IsServerConfigured: true,
            ServerHost: profile.Host);
    }

    private static SqliteSiteCache CreateCache() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SiteManager",
        "cache.db"));

    private static ServerProfile CreateDefaultProfile() => new(
        "47.86.89.203",
        22,
        "sitepublisher",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "site_manager_ed25519"),
        "SHA256:ZrZ2SF13RvyeSsLMuHl27GIelk8Yb09f1PBBae/1tbU",
        "http://47.86.89.203/s/");

    private sealed class MemorySettingsStore : IServerProfileStore
    {
        public Task<ServerProfile?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<ServerProfile?>(null);

        public Task SaveAsync(ServerProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UnavailableSiteSyncService : ISiteSyncService
    {
        public Task<IReadOnlyList<SiteManifest>> SyncAsync(CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<SiteManifest>>(NotConfigured());
    }

    private sealed class UnavailablePublishSiteService : IPublishSiteService
    {
        public Task<SiteManifest> PublishAsync(
            PublishSiteRequest request,
            IProgress<PublishProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromException<SiteManifest>(NotConfigured());
    }

    private sealed class UnavailableRemotePublisher : IRemotePublisher
    {
        public Task<RemoteServerStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromException<RemoteServerStatus>(NotConfigured());

        public Task<RemoteUploadSession> PrepareAsync(RemotePrepareRequest request, CancellationToken cancellationToken) =>
            Task.FromException<RemoteUploadSession>(NotConfigured());

        public Task<IRemoteUploadStream> OpenUploadStreamAsync(RemoteUploadSession session, CancellationToken cancellationToken) =>
            Task.FromException<IRemoteUploadStream>(NotConfigured());

        public Task<SiteManifest> PublishAsync(RemotePublishRequest request, CancellationToken cancellationToken) =>
            Task.FromException<SiteManifest>(NotConfigured());

        public Task<IReadOnlyList<SiteManifest>> ListAsync(SiteStatus? status, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<SiteManifest>>(NotConfigured());

        public Task<SiteManifest> TrashAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) =>
            Task.FromException<SiteManifest>(NotConfigured());

        public Task<SiteManifest> RestoreAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) =>
            Task.FromException<SiteManifest>(NotConfigured());

        public Task PurgeAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) =>
            Task.FromException(NotConfigured());
    }

    private static InvalidOperationException NotConfigured() =>
        new("尚未配置服务器连接。请在“设置”中完成 SSH 连接配置后再执行此操作。");
}
