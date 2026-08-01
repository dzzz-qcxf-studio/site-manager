using SiteManager.Core.Models;
using SiteManager.Core.Storage;
using SiteManager.Core.Transfers;
using SiteManager.Infrastructure.Storage;

namespace SiteManager.Infrastructure.Tests.Storage;

public sealed class SqliteSiteCacheTests : IAsyncDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"site-manager-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task ReplaceSitesAsync_replaces_cache_in_one_transaction()
    {
        await using var cache = await CreateCacheAsync();
        await cache.ReplaceSitesAsync([CreateSite("first")], TestContext.Current.CancellationToken);

        await cache.ReplaceSitesAsync([CreateSite("second")], TestContext.Current.CancellationToken);

        var sites = await cache.GetSitesAsync(TestContext.Current.CancellationToken);
        var site = Assert.Single(sites);
        Assert.Equal("second", site.Name);
    }

    [Fact]
    public async Task SaveCheckpointAsync_round_trips_upload_id_offset_and_archive_path()
    {
        await using var cache = await CreateCacheAsync();
        var checkpoint = new TransferCheckpoint(
            Guid.Parse("0191f7d0-0000-7000-8000-000000000001"),
            Guid.Parse("0191f7d0-0000-7000-8000-000000000002"),
            null,
            @"C:\temp\payload.tar.gz",
            "/srv/site-manager/staging/id/payload.tar.gz.partial",
            "a".PadLeft(64, 'a'),
            123,
            64,
            DateTimeOffset.Parse("2026-08-01T12:00:00Z"));

        await cache.SaveCheckpointAsync(checkpoint, TestContext.Current.CancellationToken);

        Assert.Equal(checkpoint, await cache.GetCheckpointAsync(checkpoint.RequestId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteCheckpointAsync_is_idempotent()
    {
        await using var cache = await CreateCacheAsync();
        var requestId = Guid.NewGuid();

        await cache.DeleteCheckpointAsync(requestId, TestContext.Current.CancellationToken);
        await cache.DeleteCheckpointAsync(requestId, TestContext.Current.CancellationToken);

        Assert.Null(await cache.GetCheckpointAsync(requestId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitializeAsync_migrates_schema_version_one()
    {
        await using var cache = new SqliteSiteCache(_databasePath);

        await cache.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, await cache.GetSchemaVersionAsync(TestContext.Current.CancellationToken));
    }

    public ValueTask DisposeAsync()
    {
        File.Delete(_databasePath);
        return ValueTask.CompletedTask;
    }

    private async Task<SqliteSiteCache> CreateCacheAsync()
    {
        var cache = new SqliteSiteCache(_databasePath);
        await cache.InitializeAsync(TestContext.Current.CancellationToken);
        return cache;
    }

    private static SiteManifest CreateSite(string name) => new(
        Guid.Parse("0191f7d0-0000-7000-8000-000000000100"),
        name,
        "",
        "a8k3m2",
        SiteStatus.Live,
        1,
        10,
        new string('a', 64),
        DateTimeOffset.Parse("2026-08-01T12:00:00Z"),
        DateTimeOffset.Parse("2026-08-01T12:00:00Z"),
        null,
        null);
}
