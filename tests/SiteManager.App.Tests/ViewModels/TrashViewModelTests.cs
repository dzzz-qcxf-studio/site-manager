using SiteManager.App.ViewModels;
using SiteManager.Core.Models;

namespace SiteManager.App.Tests.ViewModels;

public sealed class TrashViewModelTests
{
    [Fact]
    public async Task Restore_refreshes_live_and_trash_lists()
    {
        var trashed = CreateSite(SiteStatus.Trash);
        var restored = trashed with { Status = SiteStatus.Live, TrashedAt = null, PurgeAt = null };
        var sync = new FakeSiteSyncService([trashed]);
        var remote = new FakeRemotePublisher
        {
            OnRestore = _ =>
            {
                sync.Sites = [restored];
                return restored;
            }
        };
        var viewModel = new TrashViewModel(sync, remote, new AlwaysConfirmService());

        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedTrashSite = trashed;
        await viewModel.RestoreCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.TrashSites);
        Assert.Equal([restored], viewModel.LiveSites);
    }

    [Fact]
    public async Task Purge_command_requires_explicit_confirmation_service()
    {
        var trashed = CreateSite(SiteStatus.Trash);
        var remote = new FakeRemotePublisher();
        var viewModel = new TrashViewModel(new FakeSiteSyncService([trashed]), remote, new NeverConfirmService())
        {
            SelectedTrashSite = trashed
        };

        await viewModel.PurgeCommand.ExecuteAsync(null);

        Assert.Empty(remote.PurgedSiteIds);
    }

    private static SiteManifest CreateSite(SiteStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new SiteManifest(Guid.NewGuid(), "产品模型", "客户预览", "alpha-one", status, 1, 12,
            new string('a', 64), now, now, status == SiteStatus.Trash ? now : null,
            status == SiteStatus.Trash ? now.AddDays(30) : null);
    }
}
