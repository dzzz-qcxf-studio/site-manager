namespace SiteManager.Core.Transfers;

public sealed record UploadProgress(long CompletedBytes, long TotalBytes);
