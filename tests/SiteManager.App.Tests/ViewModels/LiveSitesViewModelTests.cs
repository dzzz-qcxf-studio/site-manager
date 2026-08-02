using SiteManager.App.ViewModels;
using SiteManager.Core.Models;

namespace SiteManager.App.Tests.ViewModels;

public sealed class LiveSitesViewModelTests
{
    [Fact]
    public void Search_filters_name_note_and_url_case_insensitively()
    {
        var clipboard = new RecordingClipboardService();
        var viewModel = CreateViewModel(clipboard);
        var first = CreateSite("产品模型", "客户 A", "alpha-one");
        var second = CreateSite("展厅视频", "季度更新", "beta-two");
        viewModel.ReplaceSites([first, second]);

        viewModel.SearchText = "客户 a";
        Assert.Equal([first], viewModel.FilteredSites);

        viewModel.SearchText = "BETA-TWO";
        Assert.Equal([second], viewModel.FilteredSites);
    }

    [Fact]
    public async Task Copy_link_uses_selected_site_url()
    {
        var clipboard = new RecordingClipboardService();
        var viewModel = CreateViewModel(clipboard);
        var site = CreateSite("产品模型", "客户 A", "alpha-one");
        viewModel.ReplaceSites([site]);
        viewModel.SelectedSite = site;

        await viewModel.CopyLinkCommand.ExecuteAsync(null);

        Assert.Equal("http://example.test/s/alpha-one/", clipboard.Text);
    }

    [Fact]
    public async Task Open_link_uses_selected_site_url()
    {
        var browser = new RecordingBrowserService();
        var viewModel = CreateViewModel(new RecordingClipboardService(), browser);
        var site = CreateSite("产品模型", "客户 A", "alpha-one");
        viewModel.ReplaceSites([site]);
        viewModel.SelectedSite = site;

        await viewModel.OpenLinkCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("http://example.test/s/alpha-one/"), browser.LastAddress);
    }

    [Fact]
    public async Task Copy_link_surfaces_clipboard_failure_without_throwing()
    {
        var viewModel = CreateViewModel(new ThrowingClipboardService());
        var site = CreateSite("浜у搧妯″瀷", "瀹㈡埛 A", "alpha-one");
        viewModel.ReplaceSites([site]);
        viewModel.SelectedSite = site;

        await viewModel.CopyLinkCommand.ExecuteAsync(null);

        Assert.Contains("复制链接失败", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Clipboard_service_accepts_text_when_write_reports_post_commit_failure()
    {
        var backend = new PostWriteClipboardBackend("http://example.test/s/alpha-one/");
        var service = new WpfClipboardService(backend);

        await service.SetTextAsync("http://example.test/s/alpha-one/", CancellationToken.None);

        Assert.Equal(1, backend.SetAttempts);
    }

    [Fact]
    public async Task Initial_sync_replaces_cached_sites_with_remote_sites()
    {
        var cached = CreateSite("cached", "", "cached-site");
        var remote = CreateSite("remote", "", "remote-site");
        var sync = new FakeSiteSyncService([remote]);
        var viewModel = new LiveSitesViewModel(sync, new FakeRemotePublisher(), new RecordingClipboardService(),
            new RecordingBrowserService(), new FixedLinkService("http://example.test/s"));
        viewModel.ReplaceSites([cached]);

        await viewModel.StartInitialSyncAsync();

        Assert.Equal([remote], viewModel.Sites);
    }

    [Fact]
    public async Task Moving_site_to_trash_updates_the_shared_trash_view()
    {
        var live = CreateSite("live", "", "live-site");
        var trashed = live with { Status = SiteStatus.Trash, TrashedAt = DateTimeOffset.UtcNow, PurgeAt = DateTimeOffset.UtcNow.AddDays(30) };
        var sync = new FakeSiteSyncService([live]);
        var remote = new FakeRemotePublisher
        {
            OnTrash = _ =>
            {
                sync.Sites = [trashed];
                return trashed;
            }
        };
        var catalog = new SiteCatalogState();
        var liveView = new LiveSitesViewModel(sync, remote, new RecordingClipboardService(),
            new RecordingBrowserService(), new FixedLinkService("http://example.test/s"), catalog)
        {
            SelectedSite = live
        };
        var trashView = new TrashViewModel(sync, remote, new AlwaysConfirmService(), catalog);
        liveView.ReplaceSites([live]);

        await liveView.MoveToTrashCommand.ExecuteAsync(null);

        Assert.Empty(liveView.Sites);
        Assert.Equal([trashed], trashView.TrashSites);
    }

    [Fact]
    public void Update_command_requests_the_selected_site()
    {
        var viewModel = CreateViewModel(new RecordingClipboardService());
        var site = CreateSite("产品模型", "客户 A", "alpha-one");
        viewModel.ReplaceSites([site]);
        viewModel.SelectedSite = site;
        SiteManifest? requested = null;
        viewModel.UpdateRequested += selected => requested = selected;

        viewModel.UpdateCommand.Execute(null);

        Assert.Equal(site, requested);
    }

    private static LiveSitesViewModel CreateViewModel(IClipboardService clipboard, IBrowserService? browser = null) =>
        new(new FakeSiteSyncService(), new FakeRemotePublisher(), clipboard,
            browser ?? new RecordingBrowserService(), new FixedLinkService("http://example.test/s"));

    private static SiteManifest CreateSite(string name, string note, string slug)
    {
        var now = DateTimeOffset.UtcNow;
        return new SiteManifest(Guid.NewGuid(), name, note, slug, SiteStatus.Live, 1, 12,
            new string('a', 64), now, now, null, null);
    }
}
