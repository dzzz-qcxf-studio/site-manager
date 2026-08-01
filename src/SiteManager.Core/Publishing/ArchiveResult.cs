namespace SiteManager.Core.Publishing;

public sealed record ArchiveResult(
    string Path,
    long SourceBytes,
    long CompressedBytes,
    string Sha256);
