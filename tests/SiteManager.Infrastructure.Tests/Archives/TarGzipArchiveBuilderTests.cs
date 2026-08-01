using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using SiteManager.Infrastructure.Archives;

namespace SiteManager.Infrastructure.Tests.Archives;

public sealed class TarGzipArchiveBuilderTests
{
    [Fact]
    public async Task BuildAsync_creates_sorted_relative_tar_entries_and_sha256()
    {
        await using var fixture = await WebsiteFixture.CreateAsync(
            ("index.html", "ok"),
            ("assets/a.js", "1"));
        var progressValues = new List<long>();

        var result = await new TarGzipArchiveBuilder().BuildAsync(
            fixture.Root,
            fixture.Output,
            new InlineProgress<long>(progressValues.Add),
            TestContext.Current.CancellationToken);

        Assert.Equal(fixture.Output, result.Path);
        Assert.Equal(3, result.SourceBytes);
        Assert.True(result.CompressedBytes > 0);
        Assert.Equal(64, result.Sha256.Length);
        Assert.Equal(result.SourceBytes, progressValues[^1]);
        Assert.Equal(
            ["assets/a.js", "index.html"],
            await ReadEntryNamesAsync(fixture.Output, TestContext.Current.CancellationToken));

        await using var archive = File.OpenRead(fixture.Output);
        var expectedHash = Convert.ToHexString(await SHA256.HashDataAsync(
            archive,
            TestContext.Current.CancellationToken)).ToLowerInvariant();
        Assert.Equal(expectedHash, result.Sha256);
    }

    [Fact]
    public async Task BuildAsync_cancellation_deletes_incomplete_archive()
    {
        await using var fixture = await WebsiteFixture.CreateAsync(
            ("index.html", "ok"),
            ("second.txt", "more"));
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new TarGzipArchiveBuilder().BuildAsync(
                fixture.Root,
                fixture.Output,
                new InlineProgress<long>(_ => cancellation.Cancel()),
                cancellation.Token));

        Assert.False(File.Exists(fixture.Output));
    }

    [Fact]
    public async Task BuildAsync_does_not_overwrite_or_delete_existing_output()
    {
        await using var fixture = await WebsiteFixture.CreateAsync(("index.html", "ok"));
        await File.WriteAllTextAsync(
            fixture.Output,
            "keep",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IOException>(() =>
            new TarGzipArchiveBuilder().BuildAsync(
                fixture.Root,
                fixture.Output,
                null,
                TestContext.Current.CancellationToken));

        Assert.Equal("keep", await File.ReadAllTextAsync(
            fixture.Output,
            TestContext.Current.CancellationToken));
    }

    private static async Task<string[]> ReadEntryNamesAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        var names = new List<string>();
        await using var archive = File.OpenRead(archivePath);
        await using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);

        while (await reader.GetNextEntryAsync(copyData: true, cancellationToken) is { } entry)
        {
            names.Add(entry.Name);
        }

        return [.. names];
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class WebsiteFixture : IAsyncDisposable
    {
        private WebsiteFixture(string workspace)
        {
            Workspace = workspace;
            Root = System.IO.Path.Combine(workspace, "site");
            Output = System.IO.Path.Combine(workspace, "payload.tar.gz");
        }

        public string Workspace { get; }

        public string Root { get; }

        public string Output { get; }

        public static async Task<WebsiteFixture> CreateAsync(params (string Path, string Content)[] files)
        {
            var fixture = new WebsiteFixture(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"site-manager-tests-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(fixture.Root);

            foreach (var (path, content) in files)
            {
                var fullPath = System.IO.Path.Combine(fixture.Root, path);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, content);
            }

            return fixture;
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Workspace))
            {
                Directory.Delete(Workspace, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
