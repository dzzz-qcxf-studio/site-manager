namespace SiteManager.Core.Models;

public enum SiteStatus
{
    Live,
    Trash
}

public sealed record SiteManifest(
    Guid Id,
    string Name,
    string Note,
    string Slug,
    SiteStatus Status,
    int Version,
    long SizeBytes,
    string ContentSha256,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? TrashedAt,
    DateTimeOffset? PurgeAt)
{
    public string BuildPublicUrl(string baseUrl) => $"{baseUrl.TrimEnd('/')}/{Slug}/";
}
