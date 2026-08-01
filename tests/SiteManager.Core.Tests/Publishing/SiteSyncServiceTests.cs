using SiteManager.Core.Models;
using SiteManager.Core.Publishing;
using SiteManager.Core.Storage;
using SiteManager.Core.Transfers;

namespace SiteManager.Core.Tests.Publishing;

public sealed class SiteSyncServiceTests
{
    [Fact]
    public async Task SyncAsync_replaces_local_cache_with_server_list()
    {
        var serverSites = new[] { CreateSite("first"), CreateSite("second") };
        var remote = new SyncRemotePublisher(serverSites);
        var cache = new SyncSiteCache();
        var service = new SiteSyncService(remote, cache);

        var synchronized = await service.SyncAsync(TestContext.Current.CancellationToken);

        Assert.Null(remote.RequestedStatus);
        Assert.Equal(serverSites, synchronized);
        Assert.Equal(serverSites, cache.ReplacedSites);
    }

    private static SiteManifest CreateSite(string slug)
    {
        var now = DateTimeOffset.UtcNow;
        return new SiteManifest(Guid.NewGuid(), slug, string.Empty, slug, SiteStatus.Live, 1, 0,
            new string('a', 64), now, now, null, null);
    }

    private sealed class SyncRemotePublisher(IReadOnlyList<SiteManifest> sites) : IRemotePublisher
    {
        public SiteStatus? RequestedStatus { get; private set; }

        public Task<RemoteServerStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RemoteServerStatus(DateTimeOffset.UtcNow, 0, 0));

        public Task<RemoteUploadSession> PrepareAsync(RemotePrepareRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IRemoteUploadStream> OpenUploadStreamAsync(RemoteUploadSession session, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SiteManifest> PublishAsync(RemotePublishRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SiteManifest>> ListAsync(SiteStatus? status, CancellationToken cancellationToken)
        {
            RequestedStatus = status;
            return Task.FromResult(sites);
        }

        public Task<SiteManifest> TrashAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SiteManifest> RestoreAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PurgeAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class SyncSiteCache : ISiteCache
    {
        public IReadOnlyCollection<SiteManifest>? ReplacedSites { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<SiteManifest>> GetSitesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SiteManifest>>([]);

        public Task ReplaceSitesAsync(IReadOnlyCollection<SiteManifest> sites, CancellationToken cancellationToken)
        {
            ReplacedSites = sites;
            return Task.CompletedTask;
        }

        public Task SaveCheckpointAsync(TransferCheckpoint checkpoint, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<TransferCheckpoint?> GetCheckpointAsync(Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult<TransferCheckpoint?>(null);

        public Task DeleteCheckpointAsync(Guid requestId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
