using SiteManager.Infrastructure.Storage;

namespace SiteManager.Infrastructure.Tests.Storage;

public sealed class JsonSiteFolderPathStoreTests : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"site-manager-folders-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task SetAsync_persists_path_for_update_lookup()
    {
        var siteId = Guid.NewGuid();
        var store = new JsonSiteFolderPathStore(_path);
        await store.InitializeAsync(TestContext.Current.CancellationToken);
        await store.SetAsync(siteId, "C:\\web\\demo", TestContext.Current.CancellationToken);

        var reloaded = new JsonSiteFolderPathStore(_path);
        await reloaded.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("C:\\web\\demo", reloaded.Get(siteId));
    }

    public ValueTask DisposeAsync()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        return ValueTask.CompletedTask;
    }
}
