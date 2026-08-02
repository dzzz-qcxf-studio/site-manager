using SiteManager.App.ViewModels;
using SiteManager.Core.Configuration;
using SiteManager.Core.Models;
using SiteManager.Core.Publishing;
using SiteManager.Core.Storage;
using SiteManager.Core.Transfers;
using SiteManager.Core.Validation;

namespace SiteManager.App.Tests.ViewModels;

internal sealed class FakeFolderValidator(FolderValidationResult result) : IWebsiteFolderValidator
{
    public FolderValidationResult Result { get; set; } = result;

    public FolderValidationResult Validate(string root) => Result;
}

internal sealed class FakePublishSiteService : IPublishSiteService
{
    public PublishSiteRequest? LastRequest { get; private set; }

    public Action? OnPublish { get; set; }

    public Task<SiteManifest> PublishAsync(PublishSiteRequest request, IProgress<PublishProgress>? progress, CancellationToken cancellationToken)
    {
        OnPublish?.Invoke();
        LastRequest = request;
        return Task.FromResult(TestSiteFactory.CreateLive(request.Name));
    }
}

internal sealed class BlockingPublishSiteService : IPublishSiteService
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<SiteManifest> PublishAsync(PublishSiteRequest request, IProgress<PublishProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new PublishProgress(PublishStage.Uploading, 5, 10));
        Started.SetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The cancellation token should have stopped this operation.");
    }
}

internal sealed class FixedArchivePathFactory : IArchivePathFactory
{
    public string CreatePath(Guid requestId) => Path.Combine(Path.GetTempPath(), $"{requestId:N}.tar.gz");
}

internal sealed class FakeSiteSyncService(IReadOnlyList<SiteManifest>? sites = null) : ISiteSyncService
{
    public IReadOnlyList<SiteManifest> Sites { get; set; } = sites ?? [];

    public Task<IReadOnlyList<SiteManifest>> SyncAsync(CancellationToken cancellationToken) => Task.FromResult(Sites);
}

internal sealed class FakeRemotePublisher : IRemotePublisher
{
    public Func<Guid, SiteManifest>? OnTrash { get; set; }

    public Func<Guid, SiteManifest>? OnRestore { get; set; }

    public List<Guid> PurgedSiteIds { get; } = [];

    public List<string> Calls { get; } = [];

    public Task<RemoteServerStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        Calls.Add("status");
        return Task.FromResult(new RemoteServerStatus(DateTimeOffset.UtcNow, 0, 0));
    }

    public Task<RemoteUploadSession> PrepareAsync(RemotePrepareRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IRemoteUploadStream> OpenUploadStreamAsync(RemoteUploadSession session, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<SiteManifest> PublishAsync(RemotePublishRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<SiteManifest>> ListAsync(SiteStatus? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SiteManifest>>([]);

    public Task<SiteManifest> TrashAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) =>
        Task.FromResult(OnTrash?.Invoke(siteId) ?? throw new NotSupportedException());

    public Task<SiteManifest> RestoreAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) =>
        Task.FromResult(OnRestore?.Invoke(siteId) ?? throw new NotSupportedException());

    public Task PurgeAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken)
    {
        PurgedSiteIds.Add(siteId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeRemotePublisherFactory(IRemotePublisher publisher) : IRemotePublisherFactory
{
    public IRemotePublisher Create(ServerProfile profile) => publisher;
}

internal sealed class RecordingSiteFolderPathStore(Guid siteId, string path) : ISiteFolderPathStore
{
    public Guid? LastSetSiteId { get; private set; }

    public string? LastSetPath { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public string? Get(Guid requestedSiteId) => requestedSiteId == siteId ? path : null;

    public Task SetAsync(Guid requestedSiteId, string folderPath, CancellationToken cancellationToken)
    {
        LastSetSiteId = requestedSiteId;
        LastSetPath = folderPath;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingTransferHistoryStore(IReadOnlyList<TransferHistoryEntry>? entries = null) : ITransferHistoryStore
{
    private readonly List<TransferHistoryEntry> _entries = entries?.ToList() ?? [];

    public TaskCompletionSource<TransferHistoryEntry> LastAppend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IReadOnlyList<TransferHistoryEntry>> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TransferHistoryEntry>>(_entries.ToArray());

    public Task AppendAsync(TransferHistoryEntry entry, CancellationToken cancellationToken)
    {
        _entries.Insert(0, entry);
        LastAppend.TrySetResult(entry);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingClipboardService : IClipboardService
{
    public string? Text { get; private set; }

    public Task SetTextAsync(string text, CancellationToken cancellationToken)
    {
        Text = text;
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingClipboardService : IClipboardService
{
    public Task SetTextAsync(string text, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException("OpenClipboard failed"));
}

internal sealed class PostWriteClipboardBackend(string text) : IClipboardBackend
{
    public int SetAttempts { get; private set; }

    public void SetText(string value)
    {
        SetAttempts++;
        throw new System.Runtime.InteropServices.COMException(
            "OpenClipboard failed after the text was committed.", unchecked((int)0x800401D0));
    }

    public bool TryGetText(out string? value)
    {
        value = text;
        return true;
    }
}

internal sealed class RecordingBrowserService : IBrowserService
{
    public Uri? LastAddress { get; private set; }

    public Task OpenAsync(Uri address, CancellationToken cancellationToken)
    {
        LastAddress = address;
        return Task.CompletedTask;
    }
}

internal sealed class FixedLinkService(string baseUrl) : ISiteLinkService
{
    public string Build(SiteManifest site) => site.BuildPublicUrl(baseUrl);
}

internal sealed class AlwaysConfirmService : IConfirmationService
{
    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken) => Task.FromResult(true);
}

internal sealed class NeverConfirmService : IConfirmationService
{
    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken) => Task.FromResult(false);
}

internal static class TestSiteFactory
{
    public static SiteManifest CreateLive(string name)
    {
        var now = DateTimeOffset.UtcNow;
        return new SiteManifest(Guid.NewGuid(), name, string.Empty, "created-site", SiteStatus.Live, 1, 0,
            new string('a', 64), now, now, null, null);
    }
}
