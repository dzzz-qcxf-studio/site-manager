namespace SiteManager.Core.Validation;

public sealed class WebsiteFolderValidator : IWebsiteFolderValidator
{
    public const long DefaultMaximumBytes = 2L * 1024 * 1024 * 1024;

    private readonly long _maximumBytes;

    public WebsiteFolderValidator(long maximumBytes = DefaultMaximumBytes)
    {
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        _maximumBytes = maximumBytes;
    }

    public FolderValidationResult Validate(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Website folder does not exist: {fullRoot}");
        }

        var issues = new List<ValidationIssue>();
        var rootAttributes = File.GetAttributes(fullRoot);
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            issues.Add(new ValidationIssue(
                "REPARSE_POINT",
                ".",
                "The website root cannot be a reparse point.",
                true));
            return new FolderValidationResult(0, 0, issues);
        }

        long totalBytes = 0;
        var fileCount = 0;
        var hasLowercaseRootIndex = false;
        var directories = new Stack<string>();
        directories.Push(fullRoot);

        while (directories.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(fullRoot, path));
                var isDirectory = (attributes & FileAttributes.Directory) != 0;

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    issues.Add(new ValidationIssue(
                        "REPARSE_POINT",
                        relativePath,
                        "Reparse points and symbolic links are not allowed.",
                        true));
                    continue;
                }

                if (IsSensitive(relativePath, isDirectory))
                {
                    issues.Add(new ValidationIssue(
                        "SENSITIVE_FILE",
                        relativePath,
                        "Sensitive files and directories cannot be published.",
                        true));

                    if (isDirectory)
                    {
                        continue;
                    }
                }

                if (isDirectory)
                {
                    if (string.Equals(Path.GetFileName(path), "node_modules", StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new ValidationIssue(
                            "NODE_MODULES",
                            relativePath,
                            "node_modules increases upload size and is usually unnecessary.",
                            false));
                    }

                    directories.Push(path);
                    continue;
                }

                if (string.Equals(directory, fullRoot, StringComparison.Ordinal)
                    && string.Equals(Path.GetFileName(path), "index.html", StringComparison.Ordinal))
                {
                    hasLowercaseRootIndex = true;
                }

                totalBytes += new FileInfo(path).Length;
                fileCount++;
            }
        }

        if (!hasLowercaseRootIndex)
        {
            issues.Add(new ValidationIssue(
                "INDEX_MISSING",
                "index.html",
                "The website root must contain a lowercase index.html file.",
                true));
        }

        if (totalBytes > _maximumBytes)
        {
            issues.Add(new ValidationIssue(
                "TOO_LARGE",
                ".",
                $"Website content exceeds the {_maximumBytes} byte limit.",
                true));
        }

        return new FolderValidationResult(totalBytes, fileCount, issues);
    }

    private static bool IsSensitive(string relativePath, bool isDirectory)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment =>
                string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, ".ssh", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (isDirectory || segments.Length == 0)
        {
            return false;
        }

        var fileName = segments[^1];
        return string.Equals(fileName, ".env", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "id_rsa", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "id_ed25519", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
}
