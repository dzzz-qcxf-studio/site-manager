using SiteManager.Core.Transfers;

namespace SiteManager.Core.Storage;

public interface ITransferHistoryStore
{
    Task<IReadOnlyList<TransferHistoryEntry>> GetAsync(CancellationToken cancellationToken);

    Task AppendAsync(TransferHistoryEntry entry, CancellationToken cancellationToken);
}
