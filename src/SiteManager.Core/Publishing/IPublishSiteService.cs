using SiteManager.Core.Models;

namespace SiteManager.Core.Publishing;

public interface IPublishSiteService
{
    Task<SiteManifest> PublishAsync(
        PublishSiteRequest request,
        IProgress<PublishProgress>? progress,
        CancellationToken cancellationToken);
}
