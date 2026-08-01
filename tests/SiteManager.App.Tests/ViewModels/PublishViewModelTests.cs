using SiteManager.App.ViewModels;
using SiteManager.Core.Publishing;
using SiteManager.Core.Validation;

namespace SiteManager.App.Tests.ViewModels;

public sealed class PublishViewModelTests
{
    [Fact]
    public void Publish_is_disabled_until_folder_is_valid_and_name_present()
    {
        var validator = new FakeFolderValidator(new FolderValidationResult(0, 0,
        [new ValidationIssue("INDEX_MISSING", "index.html", "Missing.", true)]));
        var viewModel = new PublishViewModel(validator, new FakePublishSiteService(), new TransferCenterViewModel(), new FixedArchivePathFactory());

        viewModel.FolderPath = "C:\\example";
        viewModel.ValidateFolder();
        viewModel.Name = "展示页";
        Assert.False(viewModel.PublishCommand.CanExecute(null));

        validator.Result = new FolderValidationResult(8, 1, []);
        viewModel.ValidateFolder();

        Assert.True(viewModel.PublishCommand.CanExecute(null));
    }

    [Fact]
    public void ValidateFolder_exposes_the_first_validation_error()
    {
        var validator = new FakeFolderValidator(new FolderValidationResult(0, 0,
            [new ValidationIssue("INDEX_MISSING", "index.html", "Missing entry point.", true)]));
        var viewModel = new PublishViewModel(
            validator,
            new FakePublishSiteService(),
            new TransferCenterViewModel(),
            new FixedArchivePathFactory())
        {
            FolderPath = "C:\\example"
        };

        viewModel.ValidateFolder();

        Assert.Equal("Missing entry point.", viewModel.ErrorMessage);
        Assert.True(viewModel.HasError);

        viewModel.FolderPath = "C:\\valid-example";
        validator.Result = new FolderValidationResult(8, 1, []);
        viewModel.ValidateFolder();

        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Publish_command_exposes_stage_and_cancellation()
    {
        var publisher = new BlockingPublishSiteService();
        var viewModel = new PublishViewModel(
            new FakeFolderValidator(new FolderValidationResult(8, 1, [])),
            publisher,
            new TransferCenterViewModel(),
            new FixedArchivePathFactory())
        {
            FolderPath = "C:\\example",
            Name = "展示页"
        };
        viewModel.ValidateFolder();

        var publishing = viewModel.PublishCommand.ExecuteAsync(null);
        await publisher.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(PublishStage.Uploading, viewModel.CurrentStage);

        viewModel.CancelCommand.Execute(null);
        await publishing;

        Assert.Equal(PublishStage.Cancelled, viewModel.CurrentStage);
        Assert.False(viewModel.IsPublishing);
    }

    [Fact]
    public async Task BeginUpdate_passes_the_existing_site_id_to_the_publish_use_case()
    {
        var publisher = new FakePublishSiteService();
        var viewModel = new PublishViewModel(
            new FakeFolderValidator(new FolderValidationResult(8, 1, [])),
            publisher,
            new TransferCenterViewModel(),
            new FixedArchivePathFactory());
        var existing = new SiteManager.Core.Models.SiteManifest(
            Guid.NewGuid(), "原名称", "原备注", "unchanged-slug", SiteManager.Core.Models.SiteStatus.Live, 1, 8,
            new string('a', 64), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);
        viewModel.BeginUpdate(existing);
        viewModel.FolderPath = "C:\\example";
        viewModel.ValidateFolder();

        await viewModel.PublishCommand.ExecuteAsync(null);

        Assert.Equal(existing.Id, publisher.LastRequest!.ExistingSiteId);
        Assert.Equal("原名称", viewModel.Name);
    }

    [Fact]
    public async Task Publish_persists_the_source_folder_for_the_published_site()
    {
        var store = new RecordingSiteFolderPathStore(Guid.NewGuid(), "C:\\unused");
        var viewModel = new PublishViewModel(
            new FakeFolderValidator(new FolderValidationResult(8, 1, [])),
            new FakePublishSiteService(),
            new TransferCenterViewModel(),
            new FixedArchivePathFactory(),
            store)
        {
            FolderPath = "C:\\web\\new",
            Name = "新项目"
        };
        viewModel.ValidateFolder();

        await viewModel.PublishCommand.ExecuteAsync(null);

        Assert.NotNull(store.LastSetSiteId);
        Assert.Equal("C:\\web\\new", store.LastSetPath);
    }

    [Fact]
    public async Task Update_persists_the_source_folder_before_remote_work_starts()
    {
        var existing = new SiteManager.Core.Models.SiteManifest(
            Guid.NewGuid(), "原名称", "原备注", "unchanged-slug", SiteManager.Core.Models.SiteStatus.Live, 1, 8,
            new string('a', 64), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);
        var store = new RecordingSiteFolderPathStore(existing.Id, string.Empty);
        var persistedBeforeRemoteWork = false;
        var publisher = new FakePublishSiteService
        {
            OnPublish = () => persistedBeforeRemoteWork = store.LastSetPath == "C:\\web\\update"
        };
        var viewModel = new PublishViewModel(
            new FakeFolderValidator(new FolderValidationResult(8, 1, [])),
            publisher,
            new TransferCenterViewModel(),
            new FixedArchivePathFactory(),
            store)
        {
            FolderPath = "C:\\web\\update"
        };
        viewModel.BeginUpdate(existing);
        viewModel.FolderPath = "C:\\web\\update";
        viewModel.ValidateFolder();

        await viewModel.PublishCommand.ExecuteAsync(null);

        Assert.True(persistedBeforeRemoteWork);
    }

    [Fact]
    public void BeginNew_clears_update_state_and_form_values()
    {
        var viewModel = new PublishViewModel(
            new FakeFolderValidator(new FolderValidationResult(8, 1, [])),
            new FakePublishSiteService(),
            new TransferCenterViewModel(),
            new FixedArchivePathFactory());
        var existing = new SiteManager.Core.Models.SiteManifest(
            Guid.NewGuid(), "原名称", "原备注", "unchanged-slug", SiteManager.Core.Models.SiteStatus.Live, 1, 8,
            new string('a', 64), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);

        viewModel.BeginUpdate(existing);
        viewModel.FolderPath = "C:\\example";
        viewModel.BeginNew();

        Assert.Null(viewModel.ExistingSiteId);
        Assert.Equal("上架新网站", viewModel.OperationTitle);
        Assert.Empty(viewModel.FolderPath);
        Assert.Empty(viewModel.Name);
        Assert.Empty(viewModel.Note);
        Assert.Null(viewModel.PublishedSite);
    }

    [Fact]
    public void BeginUpdate_restores_the_last_source_folder_for_the_site()
    {
        var existing = new SiteManager.Core.Models.SiteManifest(
            Guid.NewGuid(), "原名称", "原备注", "unchanged-slug", SiteManager.Core.Models.SiteStatus.Live, 1, 8,
            new string('a', 64), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);
        var folders = new RecordingSiteFolderPathStore(existing.Id, "C:\\web\\previous");
        var viewModel = new PublishViewModel(
            new FakeFolderValidator(new FolderValidationResult(8, 1, [])),
            new FakePublishSiteService(),
            new TransferCenterViewModel(),
            new FixedArchivePathFactory(),
            folders);

        viewModel.BeginUpdate(existing);

        Assert.Equal("C:\\web\\previous", viewModel.FolderPath);
    }

    [Fact]
    public async Task Publish_requests_navigation_to_transfer_center_before_work_starts()
    {
        var order = new List<string>();
        var publisher = new FakePublishSiteService { OnPublish = () => order.Add("publish") };
        var viewModel = new PublishViewModel(
            new FakeFolderValidator(new FolderValidationResult(8, 1, [])),
            publisher,
            new TransferCenterViewModel(),
            new FixedArchivePathFactory())
        {
            FolderPath = "C:\\web",
            Name = "新项目"
        };
        viewModel.ValidateFolder();
        viewModel.TransferRequested += () => order.Add("transfer");

        await viewModel.PublishCommand.ExecuteAsync(null);

        Assert.Equal(["transfer", "publish"], order);
    }
}
