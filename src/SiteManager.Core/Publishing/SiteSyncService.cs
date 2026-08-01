using SiteManager.Core.Models;
using SiteManager.Core.Storage;

namespace SiteManager.Core.Publishing;

public sealed class SiteSyncService : ISiteSyncService
{
    private readonly IRemotePublisher _remotePublisher;
    private readonly ISiteCache _siteCache;

    public SiteSyncService(IRemotePublisher remotePublisher, ISiteCache siteCache)
    {
        _remotePublisher = remotePublisher ?? throw new ArgumentNullException(nameof(remotePublisher));
        _siteCache = siteCache ?? throw new ArgumentNullException(nameof(siteCache));
    }

    public async Task<IReadOnlyList<SiteManifest>> SyncAsync(CancellationToken cancellationToken)
    {
        var sites = await _remotePublisher.ListAsync(status: null, cancellationToken);
        await _siteCache.ReplaceSitesAsync(sites.ToArray(), cancellationToken);
        return sites;
    }
}
