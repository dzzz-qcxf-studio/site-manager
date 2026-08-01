namespace SiteManager.Core.Publishing;

public enum PublishStage
{
    Scanning,
    Archiving,
    Preparing,
    Uploading,
    Verifying,
    Publishing,
    Completed,
    Failed,
    Cancelled
}

public sealed record PublishProgress(PublishStage Stage, long CompletedBytes = 0, long TotalBytes = 0);
