using SiteManager.Core.Models;
using SiteManager.Core.Publishing;

namespace SiteManager.App.ViewModels;

/// <summary>
/// Keeps the live and trash pages on one snapshot and serializes remote mutations.
/// </summary>
public sealed class SiteCatalogState
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private IReadOnlyList<SiteManifest> _sites = [];

    public IReadOnlyList<SiteManifest> Sites => _sites;

    public event Action<IReadOnlyList<SiteManifest>>? Changed;

    public void ReplaceSites(IEnumerable<SiteManifest> sites)
    {
        ArgumentNullException.ThrowIfNull(sites);
        _sites = sites.ToArray();
        Changed?.Invoke(_sites);
    }

    public async Task<IReadOnlyList<SiteManifest>> SyncAndReplaceAsync(
        ISiteSyncService siteSyncService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siteSyncService);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var sites = await siteSyncService.SyncAsync(cancellationToken);
            ReplaceSites(sites);
            return _sites;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<SiteManifest>> MutateAndSyncAsync(
        ISiteSyncService siteSyncService,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(siteSyncService);
        ArgumentNullException.ThrowIfNull(mutation);
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            await mutation(cancellationToken);
            var sites = await siteSyncService.SyncAsync(cancellationToken);
            ReplaceSites(sites);
            return _sites;
        }
        finally
        {
            _operationGate.Release();
        }
    }
}
