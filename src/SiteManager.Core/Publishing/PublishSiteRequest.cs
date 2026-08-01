namespace SiteManager.Core.Publishing;

public sealed record PublishSiteRequest(
    Guid RequestId,
    string SourceDirectory,
    string ArchivePath,
    string Name,
    string Note,
    Guid? ExistingSiteId)
{
    public bool IsUpdate => ExistingSiteId is not null;
}
