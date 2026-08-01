using SiteManager.App.ViewModels;
using SiteManager.Core.Configuration;
using SiteManager.Core.Publishing;

namespace SiteManager.App.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task TestConnection_calls_status_only()
    {
        var remotePublisher = new FakeRemotePublisher();
        var tester = new RemoteConnectionTester(new FakeRemotePublisherFactory(remotePublisher));
        var viewModel = new SettingsViewModel(new MemorySettingsStore(), tester, CreateProfile());

        await viewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(["status"], remotePublisher.Calls);
        Assert.Contains("连接成功", viewModel.StatusMessage);
    }

    [Fact]
    public void Save_is_disabled_for_invalid_profile()
    {
        var viewModel = new SettingsViewModel(new MemorySettingsStore(), new StatusOnlyConnectionTester(), CreateProfile())
        {
            Host = "invalid host"
        };

        Assert.False(viewModel.SaveCommand.CanExecute(null));
    }

    private static ServerProfile CreateProfile() => new(
        "47.86.89.203", 22, "sitepublisher", "C:\\Users\\ROG\\.ssh\\site_manager_ed25519",
        "SHA256:ZrZ2SF13RvyeSsLMuHl27GIelk8Yb09f1PBBae/1tbU", "http://47.86.89.203/s/");

    private sealed class MemorySettingsStore : IServerProfileStore
    {
        public ServerProfile? SavedProfile { get; private set; }

        public Task<ServerProfile?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(SavedProfile);

        public Task SaveAsync(ServerProfile profile, CancellationToken cancellationToken)
        {
            SavedProfile = profile;
            return Task.CompletedTask;
        }
    }

    private sealed class StatusOnlyConnectionTester : IConnectionTester
    {
        public Task<RemoteServerStatus> TestAsync(ServerProfile profile, CancellationToken cancellationToken) =>
            Task.FromResult(new RemoteServerStatus(DateTimeOffset.UtcNow, 100, 50));
    }
}
