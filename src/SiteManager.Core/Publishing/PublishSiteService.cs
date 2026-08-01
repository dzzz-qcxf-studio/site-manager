using SiteManager.Core.Models;
using SiteManager.Core.Storage;
using SiteManager.Core.Transfers;
using SiteManager.Core.Validation;

namespace SiteManager.Core.Publishing;

public sealed class PublishSiteService : IPublishSiteService
{
    private const int StreamBufferSize = 1024 * 1024;

    private readonly IWebsiteFolderValidator _validator;
    private readonly IArchiveBuilder _archiveBuilder;
    private readonly IRemotePublisher _remotePublisher;
    private readonly ISiteCache _siteCache;
    private readonly ResumableUploadEngine _uploadEngine;

    public PublishSiteService(
        IWebsiteFolderValidator validator,
        IArchiveBuilder archiveBuilder,
        IRemotePublisher remotePublisher,
        ISiteCache siteCache,
        ResumableUploadEngine uploadEngine)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _archiveBuilder = archiveBuilder ?? throw new ArgumentNullException(nameof(archiveBuilder));
        _remotePublisher = remotePublisher ?? throw new ArgumentNullException(nameof(remotePublisher));
        _siteCache = siteCache ?? throw new ArgumentNullException(nameof(siteCache));
        _uploadEngine = uploadEngine ?? throw new ArgumentNullException(nameof(uploadEngine));
    }

    public async Task<SiteManifest> PublishAsync(
        PublishSiteRequest request,
        IProgress<PublishProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, PublishStage.Scanning);
            var validation = _validator.Validate(request.SourceDirectory);
            if (!validation.IsValid)
            {
                throw new WebsiteValidationException(validation);
            }

            Report(progress, PublishStage.Archiving, 0, validation.TotalBytes);
            var archive = await _archiveBuilder.BuildAsync(
                request.SourceDirectory,
                request.ArchivePath,
                new ArchiveProgress(progress, validation.TotalBytes),
                cancellationToken);

            Report(progress, PublishStage.Preparing);
            var prepare = new RemotePrepareRequest(
                request.RequestId,
                request.ExistingSiteId,
                archive.CompressedBytes,
                archive.Sha256);
            var session = await _remotePublisher.PrepareAsync(prepare, cancellationToken);
            ValidateSession(session);

            var checkpoint = new TransferCheckpoint(
                request.RequestId,
                session.UploadId,
                request.ExistingSiteId,
                archive.Path,
                session.RemotePath,
                archive.Sha256,
                archive.CompressedBytes,
                session.ResumeOffset,
                DateTimeOffset.UtcNow);
            await _siteCache.SaveCheckpointAsync(checkpoint, cancellationToken);

            Report(progress, PublishStage.Uploading, session.ResumeOffset, archive.CompressedBytes);
            await using var remoteStream = await _remotePublisher.OpenUploadStreamAsync(session, cancellationToken);
            await using var archiveStream = new FileStream(
                archive.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await _uploadEngine.UploadAsync(
                archiveStream,
                remoteStream,
                new UploadStageProgress(progress),
                cancellationToken);

            Report(progress, PublishStage.Verifying, archive.CompressedBytes, archive.CompressedBytes);
            Report(progress, PublishStage.Publishing, archive.CompressedBytes, archive.CompressedBytes);
            var published = await _remotePublisher.PublishAsync(
                new RemotePublishRequest(
                    request.RequestId,
                    session.UploadId,
                    request.ExistingSiteId,
                    request.Name,
                    request.Note),
                cancellationToken);

            var cachedSites = await _siteCache.GetSitesAsync(cancellationToken);
            var updatedSites = cachedSites
                .Where(site => site.Id != published.Id)
                .Append(published)
                .OrderByDescending(site => site.UpdatedAt)
                .ToArray();
            await _siteCache.ReplaceSitesAsync(updatedSites, cancellationToken);
            await _siteCache.DeleteCheckpointAsync(request.RequestId, cancellationToken);

            Report(progress, PublishStage.Completed, archive.CompressedBytes, archive.CompressedBytes);
            return published;
        }
        catch (OperationCanceledException)
        {
            Report(progress, PublishStage.Cancelled);
            throw;
        }
        catch
        {
            Report(progress, PublishStage.Failed);
            throw;
        }
    }

    private static void ValidateRequest(PublishSiteRequest request)
    {
        if (request.RequestId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty request ID is required.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ArchivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentNullException.ThrowIfNull(request.Note);
    }

    private static void ValidateSession(RemoteUploadSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.UploadId == Guid.Empty)
        {
            throw new InvalidDataException("Remote upload session did not contain an upload ID.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(session.RemotePath);
        if (session.ResumeOffset < 0)
        {
            throw new InvalidDataException("Remote upload session had a negative resume offset.");
        }
    }

    private static void Report(IProgress<PublishProgress>? progress, PublishStage stage, long completedBytes = 0, long totalBytes = 0) =>
        progress?.Report(new PublishProgress(stage, completedBytes, totalBytes));

    private sealed class ArchiveProgress(IProgress<PublishProgress>? progress, long totalBytes) : IProgress<long>
    {
        public void Report(long value) => PublishSiteService.Report(progress, PublishStage.Archiving, value, totalBytes);
    }

    private sealed class UploadStageProgress(IProgress<PublishProgress>? progress) : IProgress<UploadProgress>
    {
        public void Report(UploadProgress value) =>
            PublishSiteService.Report(progress, PublishStage.Uploading, value.CompletedBytes, value.TotalBytes);
    }
}
