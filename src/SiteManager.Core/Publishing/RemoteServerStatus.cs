namespace SiteManager.Core.Publishing;

public sealed record RemoteServerStatus(
    DateTimeOffset ServerTime,
    long TotalBytes,
    long FreeBytes);
