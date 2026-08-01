namespace SiteManager.Core.Transfers;

public sealed record TransferCheckpoint(
    Guid RequestId,
    Guid UploadId,
    Guid? SiteId,
    string ArchivePath,
    string RemotePath,
    string ExpectedSha256,
    long TotalBytes,
    long RemoteOffset,
    DateTimeOffset UpdatedAt);
