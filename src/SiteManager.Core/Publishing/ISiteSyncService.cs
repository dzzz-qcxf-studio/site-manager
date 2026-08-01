using SiteManager.Core.Models;

namespace SiteManager.Core.Publishing;

public interface ISiteSyncService
{
    Task<IReadOnlyList<SiteManifest>> SyncAsync(CancellationToken cancellationToken);
}
