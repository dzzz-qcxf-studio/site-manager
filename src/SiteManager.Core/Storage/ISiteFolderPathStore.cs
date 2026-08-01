namespace SiteManager.Core.Storage;

public interface ISiteFolderPathStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    string? Get(Guid siteId);

    Task SetAsync(Guid siteId, string folderPath, CancellationToken cancellationToken);
}
