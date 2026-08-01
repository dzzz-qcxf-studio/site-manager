using SiteManager.App.ViewModels;

namespace SiteManager.App.Tests.ViewModels;

public sealed class ShellViewModelTests
{
    [Fact]
    public void Default_section_is_live_sites()
    {
        var viewModel = new ShellViewModel();

        Assert.Equal(AppSection.LiveSites, viewModel.CurrentSection);
        Assert.Equal("已上架网站", viewModel.CurrentTitle);
        Assert.Equal("服务器尚未配置", viewModel.ServerStatusTitle);
        Assert.Equal("打开设置页完成 SSH 连接信息", viewModel.ServerStatusDescription);
    }

    [Fact]
    public void Navigate_changes_section_title_and_selection_state()
    {
        var viewModel = new ShellViewModel();

        viewModel.NavigateCommand.Execute(AppSection.Publish);

        Assert.Equal(AppSection.Publish, viewModel.CurrentSection);
        Assert.Equal("上架网站", viewModel.CurrentTitle);
        Assert.True(viewModel.IsSelected(AppSection.Publish));
        Assert.False(viewModel.IsSelected(AppSection.LiveSites));
        Assert.Same(viewModel.Publish, viewModel.CurrentPage);
    }

    [Fact]
    public void Navigating_to_publish_starts_a_new_site_instead_of_reusing_update_state()
    {
        var viewModel = new ShellViewModel();
        var existing = new SiteManager.Core.Models.SiteManifest(
            Guid.NewGuid(), "原名称", "原备注", "unchanged-slug", SiteManager.Core.Models.SiteStatus.Live, 1, 8,
            new string('a', 64), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);

        viewModel.Publish.BeginUpdate(existing);
        viewModel.NavigateCommand.Execute(AppSection.Publish);

        Assert.Null(viewModel.Publish.ExistingSiteId);
        Assert.Equal("上架新网站", viewModel.Publish.OperationTitle);
    }
}
