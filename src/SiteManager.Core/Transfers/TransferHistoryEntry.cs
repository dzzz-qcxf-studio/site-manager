using SiteManager.Core.Publishing;

namespace SiteManager.Core.Transfers;

public sealed record TransferHistoryEntry(
    Guid RequestId,
    string Name,
    string SourceFolderPath,
    PublishStage FinalStage,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    long CompletedBytes,
    long TotalBytes)
{
    public bool IsSuccess => FinalStage == PublishStage.Completed;

    public string StatusText => FinalStage switch
    {
        PublishStage.Completed => "成功",
        PublishStage.Cancelled => "已取消",
        PublishStage.Failed => "失败",
        _ => FinalStage.ToString()
    };
}
