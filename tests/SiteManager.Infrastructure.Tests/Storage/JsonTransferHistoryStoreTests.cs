using SiteManager.Core.Publishing;
using SiteManager.Core.Transfers;
using SiteManager.Infrastructure.Storage;

namespace SiteManager.Infrastructure.Tests.Storage;

public sealed class JsonTransferHistoryStoreTests : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"site-manager-history-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task AppendAsync_round_trips_entries_in_newest_first_order()
    {
        var store = new JsonTransferHistoryStore(_path);
        var older = CreateEntry("older", DateTimeOffset.UtcNow.AddMinutes(-2));
        var newer = CreateEntry("newer", DateTimeOffset.UtcNow);

        await store.AppendAsync(older, TestContext.Current.CancellationToken);
        await store.AppendAsync(newer, TestContext.Current.CancellationToken);

        var entries = await new JsonTransferHistoryStore(_path).GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal([newer, older], entries);
    }

    public ValueTask DisposeAsync()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        return ValueTask.CompletedTask;
    }

    private static TransferHistoryEntry CreateEntry(string name, DateTimeOffset completedAt) => new(
        Guid.NewGuid(), name, "C:\\web", PublishStage.Completed, completedAt.AddMinutes(-1), completedAt, 10, 10);
}
