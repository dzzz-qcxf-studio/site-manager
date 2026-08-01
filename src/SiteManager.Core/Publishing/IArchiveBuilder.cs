namespace SiteManager.Core.Publishing;

public interface IArchiveBuilder
{
    Task<ArchiveResult> BuildAsync(
        string sourceDirectory,
        string outputPath,
        IProgress<long>? progress,
        CancellationToken cancellationToken);
}
