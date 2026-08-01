using SiteManager.App.ViewModels;
using SiteManager.Core.Publishing;
using SiteManager.Core.Storage;
using SiteManager.Core.Transfers;

namespace SiteManager.App.Tests.ViewModels;

public sealed class TransferCenterViewModelTests
{
    [Fact]
    public async Task Initialize_loads_history_and_terminal_progress_adds_success_entry()
    {
        var stored = new TransferHistoryEntry(
            Guid.NewGuid(), "历史项目", "C:\\web\\history", PublishStage.Failed,
            DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(-1), 5, 10);
        var history = new RecordingTransferHistoryStore([stored]);
        var viewModel = new TransferCenterViewModel(history);

        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        viewModel.Begin("新项目", Guid.NewGuid(), "C:\\web\\new");
        viewModel.Report(new PublishProgress(PublishStage.Completed, 20, 20));
        await history.LastAppend.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, viewModel.History.Count);
        Assert.Equal("新项目", viewModel.History[0].Name);
        Assert.True(viewModel.History[0].IsSuccess);
        Assert.Equal("失败", viewModel.History[1].StatusText);
    }
}
