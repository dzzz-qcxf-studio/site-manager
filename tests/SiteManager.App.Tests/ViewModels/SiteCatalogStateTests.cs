using SiteManager.App.ViewModels;
using SiteManager.Core.Models;

namespace SiteManager.App.Tests.ViewModels;

public sealed class SiteCatalogStateTests
{
    [Fact]
    public async Task Mutations_are_serialized_across_pages()
    {
        var state = new SiteCatalogState();
        var sync = new FakeSiteSyncService();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximumActive = 0;
        var maximumLock = new object();
        var invocation = 0;

        async Task Mutate(CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref active);
            lock (maximumLock)
            {
                maximumActive = Math.Max(maximumActive, current);
            }
            if (Interlocked.Increment(ref invocation) == 1)
            {
                firstStarted.SetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }

            Interlocked.Decrement(ref active);
        }

        var first = state.MutateAndSyncAsync(sync, Mutate, CancellationToken.None);
        await firstStarted.Task;
        var second = state.MutateAndSyncAsync(sync, Mutate, CancellationToken.None);

        Assert.False(second.IsCompleted);
        releaseFirst.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, maximumActive);
    }

    [Fact]
    public void Replacing_catalog_notifies_all_subscribers_with_one_snapshot()
    {
        var state = new SiteCatalogState();
        IReadOnlyList<SiteManifest>? received = null;
        state.Changed += sites => received = sites;
        var site = new SiteManifest(Guid.NewGuid(), "测试", "", "test", SiteStatus.Live, 1, 0,
            new string('a', 64), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);

        state.ReplaceSites([site]);

        Assert.Same(state.Sites, received);
        Assert.Equal([site], received);
    }
}
