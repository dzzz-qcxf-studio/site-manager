using SiteManager.App.CommandLine;
using SiteManager.Core.Configuration;
using SiteManager.Core.Models;
using SiteManager.Core.Publishing;
using SiteManager.Core.Storage;
using SiteManager.Core.Transfers;

namespace SiteManager.App.Tests.CommandLine;

public sealed class CliRunnerTests
{
    [Fact]
    public async Task Help_json_does_not_require_server_configuration()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var runner = new CliRunner(settingsStoreFactory: _ => new NullSettingsStore());

        var exitCode = await runner.RunAsync(["help", "--json"], stdout, stderr, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("\"command\": \"help\"", stdout.ToString());
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public async Task Missing_configuration_is_structured_for_ai()
    {
        var stdout = new StringWriter();
        var runner = new CliRunner(settingsStoreFactory: _ => new NullSettingsStore());

        var exitCode = await runner.RunAsync(["list", "--json"], stdout, TextWriter.Null, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.OperationError, exitCode);
        Assert.Contains("NOT_CONFIGURED", stdout.ToString());
    }

    [Fact]
    public async Task List_json_returns_remote_sites_and_public_urls()
    {
        var site = CreateSite();
        var remote = new TestRemotePublisher([site]);
        var cache = new MemoryCache();
        var runner = new CliRunner(
            settingsStoreFactory: _ => new ProfileStore(CreateProfile()),
            remotePublisherFactory: new TestRemotePublisherFactory(remote),
            cacheFactory: _ => cache);
        var stdout = new StringWriter();

        var exitCode = await runner.RunAsync(["list", "--json", "--status", "live"], stdout, TextWriter.Null, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("alpha-one", stdout.ToString());
        Assert.Contains("http://example.test/s/alpha-one/", stdout.ToString());
        Assert.Single(cache.Sites);
    }

    [Fact]
    public async Task Purge_requires_explicit_confirmation_without_remote_mutation()
    {
        var site = CreateSite() with { Status = SiteStatus.Trash };
        var remote = new TestRemotePublisher([site]);
        var runner = new CliRunner(
            settingsStoreFactory: _ => new ProfileStore(CreateProfile()),
            remotePublisherFactory: new TestRemotePublisherFactory(remote),
            cacheFactory: _ => new MemoryCache());
        var stdout = new StringWriter();

        var exitCode = await runner.RunAsync(["purge", "--json", "--site", "alpha-one"], stdout, TextWriter.Null, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.SelectionError, exitCode);
        Assert.Contains("CONFIRMATION_REQUIRED", stdout.ToString());
        Assert.Equal(0, remote.PurgeCalls);
    }

    private sealed class NullSettingsStore : IServerProfileStore
    {
        public Task<ServerProfile?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<ServerProfile?>(null);

        public Task SaveAsync(ServerProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ProfileStore(ServerProfile profile) : IServerProfileStore
    {
        public Task<ServerProfile?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<ServerProfile?>(profile);

        public Task SaveAsync(ServerProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestRemotePublisherFactory(IRemotePublisher publisher) : IRemotePublisherFactory
    {
        public IRemotePublisher Create(ServerProfile profile) => publisher;
    }

    private sealed class TestRemotePublisher(IReadOnlyList<SiteManifest> sites) : IRemotePublisher
    {
        public int PurgeCalls { get; private set; }

        public Task<RemoteServerStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RemoteServerStatus(DateTimeOffset.UtcNow, 100, 50));

        public Task<RemoteUploadSession> PrepareAsync(RemotePrepareRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IRemoteUploadStream> OpenUploadStreamAsync(RemoteUploadSession session, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SiteManifest> PublishAsync(RemotePublishRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SiteManifest>> ListAsync(SiteStatus? status, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SiteManifest>>(sites.Where(site => status is null || site.Status == status).ToArray());

        public Task<SiteManifest> TrashAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SiteManifest> RestoreAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task PurgeAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken)
        {
            PurgeCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryCache : ISiteCache
    {
        public IReadOnlyList<SiteManifest> Sites { get; private set; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<SiteManifest>> GetSitesAsync(CancellationToken cancellationToken) => Task.FromResult(Sites);

        public Task ReplaceSitesAsync(IReadOnlyCollection<SiteManifest> sites, CancellationToken cancellationToken)
        {
            Sites = sites.ToArray();
            return Task.CompletedTask;
        }

        public Task SaveCheckpointAsync(TransferCheckpoint checkpoint, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TransferCheckpoint?> GetCheckpointAsync(Guid requestId, CancellationToken cancellationToken) => Task.FromResult<TransferCheckpoint?>(null);

        public Task DeleteCheckpointAsync(Guid requestId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static ServerProfile CreateProfile() => new(
        "example.test",
        22,
        "sitepublisher",
        "C:\\keys\\site-manager-ed25519",
        "SHA256:ZrZ2SF13RvyeSsLMuHl27GIelk8Yb09f1PBBae/1tbU",
        "http://example.test/s/");

    private static SiteManifest CreateSite() => new(
        Guid.NewGuid(),
        "演示项目",
        "",
        "alpha-one",
        SiteStatus.Live,
        1,
        12,
        new string('a', 64),
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        null,
        null);
}
