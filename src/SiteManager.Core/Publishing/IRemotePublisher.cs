using SiteManager.Core.Models;
using SiteManager.Core.Transfers;

namespace SiteManager.Core.Publishing;

public interface IRemotePublisher
{
    Task<RemoteServerStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<RemoteUploadSession> PrepareAsync(RemotePrepareRequest request, CancellationToken cancellationToken);

    Task<IRemoteUploadStream> OpenUploadStreamAsync(RemoteUploadSession session, CancellationToken cancellationToken);

    Task<SiteManifest> PublishAsync(RemotePublishRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<SiteManifest>> ListAsync(SiteStatus? status, CancellationToken cancellationToken);

    Task<SiteManifest> TrashAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken);

    Task<SiteManifest> RestoreAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken);

    Task PurgeAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken);
}

public sealed record RemotePrepareRequest(
    Guid RequestId,
    Guid? ExistingSiteId,
    long ArchiveBytes,
    string ExpectedSha256)
{
    public bool IsUpdate => ExistingSiteId is not null;
}

public sealed record RemoteUploadSession(
    Guid UploadId,
    string RemotePath,
    long ResumeOffset,
    DateTimeOffset ExpiresAt);

public sealed record RemotePublishRequest(
    Guid RequestId,
    Guid UploadId,
    Guid? ExistingSiteId,
    string Name,
    string Note);
