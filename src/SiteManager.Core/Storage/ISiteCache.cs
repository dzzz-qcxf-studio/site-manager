using SiteManager.Core.Models;
using SiteManager.Core.Transfers;

namespace SiteManager.Core.Storage;

public interface ISiteCache
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SiteManifest>> GetSitesAsync(CancellationToken cancellationToken);

    Task ReplaceSitesAsync(IReadOnlyCollection<SiteManifest> sites, CancellationToken cancellationToken);

    Task SaveCheckpointAsync(TransferCheckpoint checkpoint, CancellationToken cancellationToken);

    Task<TransferCheckpoint?> GetCheckpointAsync(Guid requestId, CancellationToken cancellationToken);

    Task DeleteCheckpointAsync(Guid requestId, CancellationToken cancellationToken);
}
