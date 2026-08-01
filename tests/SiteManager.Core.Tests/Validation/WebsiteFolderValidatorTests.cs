using SiteManager.Core.Validation;

namespace SiteManager.Core.Tests.Validation;

public sealed class WebsiteFolderValidatorTests
{
    [Fact]
    public void Validate_rejects_folder_without_lowercase_index_html()
    {
        using var site = TemporaryDirectory.Create();
        site.WriteFile("Index.html", "wrong case");

        var result = new WebsiteFolderValidator().Validate(site.Path);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue is { Code: "INDEX_MISSING", RelativePath: "index.html", IsError: true });
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.local")]
    [InlineData("id_rsa")]
    [InlineData("keys/id_ed25519")]
    [InlineData("certificates/client.pem")]
    [InlineData("certificates/client.key")]
    public void Validate_rejects_private_key_and_dot_env(string relativePath)
    {
        using var site = TemporaryDirectory.Create();
        site.WriteFile("index.html", "ok");
        site.WriteFile(relativePath, "secret");

        var result = new WebsiteFolderValidator().Validate(site.Path);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue is { Code: "SENSITIVE_FILE", IsError: true }
            && issue.RelativePath == relativePath);
    }

    [Theory]
    [InlineData(".git")]
    [InlineData(".ssh")]
    public void Validate_rejects_sensitive_directory_once_without_scanning_its_contents(string directoryName)
    {
        using var site = TemporaryDirectory.Create();
        site.WriteFile("index.html", "ok");
        site.WriteFile($"{directoryName}/config", "ignored");
        site.WriteFile($"{directoryName}/nested/private.key", "ignored too");

        var result = new WebsiteFolderValidator().Validate(site.Path);

        var issue = Assert.Single(result.Issues, item => item.Code == "SENSITIVE_FILE");
        Assert.Equal(directoryName, issue.RelativePath);
        Assert.True(issue.IsError);
        Assert.Equal(1, result.FileCount);
        Assert.Equal(2, result.TotalBytes);
    }

    [Fact]
    public void Validate_rejects_reparse_points_without_following_them()
    {
        using var site = TemporaryDirectory.Create();
        using var outside = TemporaryDirectory.Create();
        site.WriteFile("index.html", "ok");
        outside.WriteFile("outside.bin", new byte[32]);
        site.CreateDirectoryLink("outside-link", outside.Path);
        site.CreateDirectoryLink("self-loop", site.Path);

        var result = new WebsiteFolderValidator().Validate(site.Path);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Issues.Count(issue => issue.Code == "REPARSE_POINT"));
        Assert.DoesNotContain(result.Issues, issue => issue.RelativePath.Contains("outside.bin", StringComparison.Ordinal));
        Assert.Equal(1, result.FileCount);
        Assert.Equal(2, result.TotalBytes);
    }

    [Fact]
    public void Validate_rejects_content_over_two_gibibytes()
    {
        Assert.Equal(2L * 1024 * 1024 * 1024, WebsiteFolderValidator.DefaultMaximumBytes);
        using var site = TemporaryDirectory.Create();
        site.WriteFile("index.html", "12345");

        var result = new WebsiteFolderValidator(maximumBytes: 4).Validate(site.Path);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue is { Code: "TOO_LARGE", RelativePath: ".", IsError: true });
    }

    [Fact]
    public void Validate_accepts_normal_static_site_and_returns_totals()
    {
        using var site = TemporaryDirectory.Create();
        site.WriteFile("index.html", new byte[] { 1, 2, 3 });
        site.WriteFile("assets/app.js", new byte[] { 4, 5, 6, 7 });

        var result = new WebsiteFolderValidator().Validate(site.Path);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.FileCount);
        Assert.Equal(7, result.TotalBytes);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Validate_warns_for_node_modules_without_rejecting_site()
    {
        using var site = TemporaryDirectory.Create();
        site.WriteFile("index.html", "ok");
        site.WriteFile("node_modules/package/index.js", "module");

        var result = new WebsiteFolderValidator().Validate(site.Path);

        Assert.True(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("NODE_MODULES", issue.Code);
        Assert.Equal("node_modules", issue.RelativePath);
        Assert.False(issue.IsError);
        Assert.Equal(2, result.FileCount);
        Assert.Equal(8, result.TotalBytes);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly List<string> _directoryLinks = [];

        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"site-manager-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void WriteFile(string relativePath, string content) =>
            WriteFile(relativePath, System.Text.Encoding.UTF8.GetBytes(content));

        public void WriteFile(string relativePath, byte[] content)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, content);
        }

        public void CreateDirectoryLink(string relativePath, string targetPath)
        {
            var linkPath = System.IO.Path.Combine(Path, relativePath);
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
                _directoryLinks.Add(linkPath);
                return;
            }

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/d /c mklink /J \"{linkPath}\" \"{targetPath}\"",
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException("Could not start mklink.");

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(process.StandardError.ReadToEnd());
            }

            _directoryLinks.Add(linkPath);
        }

        public void Dispose()
        {
            foreach (var link in _directoryLinks)
            {
                Directory.Delete(link);
            }

            Directory.Delete(Path, recursive: true);
        }
    }
}
