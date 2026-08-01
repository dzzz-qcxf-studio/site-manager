namespace SiteManager.Core.Validation;

public sealed record ValidationIssue(
    string Code,
    string RelativePath,
    string Message,
    bool IsError);

public sealed record FolderValidationResult(
    long TotalBytes,
    int FileCount,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsValid => Issues.All(issue => !issue.IsError);
}
