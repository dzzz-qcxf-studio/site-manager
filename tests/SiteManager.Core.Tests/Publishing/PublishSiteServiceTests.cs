using System.Security.Cryptography;
using SiteManager.Core.Models;
using SiteManager.Core.Publishing;
using SiteManager.Core.Storage;
using SiteManager.Core.Transfers;
using SiteManager.Core.Validation;

namespace SiteManager.Core.Tests.Publishing;

public sealed class PublishSiteServiceTests
{
    [Fact]
    public async Task PublishAsync_validates_before_archive_or_network()
    {
        var calls = new List<string>();
        var service = CreateService(
            new RecordingValidator(calls, new FolderValidationResult(0, 0,
            [new ValidationIssue("INDEX_MISSING", "index.html", "Missing entry point.", true)])),
            calls,
            out _,
            out _);

        await Assert.ThrowsAsync<WebsiteValidationException>(() =>
            service.PublishAsync(CreateRequest(), null, TestContext.Current.CancellationToken));

        Assert.Equal(["validate"], calls);
    }

    [Fact]
    public async Task PublishAsync_saves_checkpoint_before_upload()
    {
        var calls = new List<string>();
        var service = CreateService(new RecordingValidator(calls, ValidFolder()), calls, out _, out _);

        await service.PublishAsync(CreateRequest(), null, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["validate", "archive", "prepare", "checkpoint-save", "upload", "publish", "cache-read", "cache-replace", "checkpoint-delete"],
            calls);
    }

    [Fact]
    public async Task PublishAsync_deletes_checkpoint_only_after_remote_publish_success()
    {
        var calls = new List<string>();
        var service = CreateService(new RecordingValidator(calls, ValidFolder()), calls, out var remote, out _);
        remote.PublishException = new InvalidOperationException("Remote publish failed.");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PublishAsync(CreateRequest(), null, TestContext.Current.CancellationToken));

        Assert.Contains("checkpoint-save", calls);
        Assert.Contains("publish", calls);
        Assert.DoesNotContain("cache-replace", calls);
        Assert.DoesNotContain("checkpoint-delete", calls);
    }

    [Fact]
    public async Task UpdateAsync_passes_existing_site_id_and_keeps_slug()
    {
        var calls = new List<string>();
        var existingSiteId = Guid.NewGuid();
        var service = CreateService(new RecordingValidator(calls, ValidFolder()), calls, out var remote, out _);
        var request = CreateRequest(existingSiteId);

        var published = await service.PublishAsync(request, null, TestContext.Current.CancellationToken);

        Assert.Equal(existingSiteId, remote.LastPrepareRequest!.ExistingSiteId);
        Assert.Equal(existingSiteId, remote.LastPublishRequest!.ExistingSiteId);
        Assert.Equal("unchanged-slug", published.Slug);
    }

    private static PublishSiteService CreateService(
        RecordingValidator validator,
        List<string> calls,
        out RecordingRemotePublisher remote,
        out RecordingSiteCache cache)
    {
        remote = new RecordingRemotePublisher(calls);
        cache = new RecordingSiteCache(calls);
        return new PublishSiteService(validator, new RecordingArchiveBuilder(calls), remote, cache, new ResumableUploadEngine());
    }

    private static PublishSiteRequest CreateRequest(Guid? existingSiteId = null) => new(
        Guid.NewGuid(),
        Path.GetTempPath(),
        Path.Combine(Path.GetTempPath(), $"site-manager-{Guid.NewGuid():N}.tar.gz"),
        "产品模型展示",
        "客户预览",
        existingSiteId);

    private static FolderValidationResult ValidFolder() => new(12, 2, []);

    private sealed class RecordingValidator(List<string> calls, FolderValidationResult result) : IWebsiteFolderValidator
    {
        public FolderValidationResult Validate(string root)
        {
            calls.Add("validate");
            return result;
        }
    }

    private sealed class RecordingArchiveBuilder(List<string> calls) : IArchiveBuilder
    {
        public Task<ArchiveResult> BuildAsync(
            string sourceDirectory,
            string outputPath,
            IProgress<long>? progress,
            CancellationToken cancellationToken)
        {
            calls.Add("archive");
            var contents = "archive"u8.ToArray();
            File.WriteAllBytes(outputPath, contents);
            var hash = Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant();
            return Task.FromResult(new ArchiveResult(outputPath, 12, contents.Length, hash));
        }
    }

    private sealed class RecordingRemotePublisher(List<string> calls) : IRemotePublisher
    {
        private static readonly Guid UploadId = Guid.Parse("10000000-0000-0000-0000-000000000001");

        public Exception? PublishException { get; set; }

        public RemotePrepareRequest? LastPrepareRequest { get; private set; }

        public RemotePublishRequest? LastPublishRequest { get; private set; }

        public Task<RemoteServerStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RemoteServerStatus(DateTimeOffset.UtcNow, 0, 0));

        public Task<RemoteUploadSession> PrepareAsync(RemotePrepareRequest request, CancellationToken cancellationToken)
        {
            calls.Add("prepare");
            LastPrepareRequest = request;
            return Task.FromResult(new RemoteUploadSession(UploadId, "/srv/site-manager/staging/payload.tar.gz.partial", 0, DateTimeOffset.UtcNow.AddHours(1)));
        }

        public Task<IRemoteUploadStream> OpenUploadStreamAsync(RemoteUploadSession session, CancellationToken cancellationToken)
        {
            calls.Add("upload");
            return Task.FromResult<IRemoteUploadStream>(new MemoryRemoteUploadStream());
        }

        public Task<SiteManifest> PublishAsync(RemotePublishRequest request, CancellationToken cancellationToken)
        {
            calls.Add("publish");
            LastPublishRequest = request;
            if (PublishException is not null)
            {
                return Task.FromException<SiteManifest>(PublishException);
            }

            var siteId = request.ExistingSiteId ?? Guid.Parse("20000000-0000-0000-0000-000000000001");
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new SiteManifest(
                siteId,
                request.Name,
                request.Note,
                "unchanged-slug",
                SiteStatus.Live,
                request.ExistingSiteId is null ? 1 : 2,
                12,
                new string('a', 64),
                now,
                now,
                null,
                null));
        }

        public Task<IReadOnlyList<SiteManifest>> ListAsync(SiteStatus? status, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SiteManifest>>([]);

        public Task<SiteManifest> TrashAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SiteManifest> RestoreAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PurgeAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingSiteCache(List<string> calls) : ISiteCache
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<SiteManifest>> GetSitesAsync(CancellationToken cancellationToken)
        {
            calls.Add("cache-read");
            return Task.FromResult<IReadOnlyList<SiteManifest>>([]);
        }

        public Task ReplaceSitesAsync(IReadOnlyCollection<SiteManifest> sites, CancellationToken cancellationToken)
        {
            calls.Add("cache-replace");
            return Task.CompletedTask;
        }

        public Task SaveCheckpointAsync(TransferCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            calls.Add("checkpoint-save");
            return Task.CompletedTask;
        }

        public Task<TransferCheckpoint?> GetCheckpointAsync(Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult<TransferCheckpoint?>(null);

        public Task DeleteCheckpointAsync(Guid requestId, CancellationToken cancellationToken)
        {
            calls.Add("checkpoint-delete");
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryRemoteUploadStream : IRemoteUploadStream
    {
        private readonly MemoryStream _stream = new();

        public Task<long> GetLengthAsync(CancellationToken cancellationToken) => Task.FromResult(_stream.Length);

        public Task SeekAsync(long offset, CancellationToken cancellationToken)
        {
            _stream.Position = offset;
            return Task.CompletedTask;
        }

        public Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            _stream.Write(buffer.Span);
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => _stream.DisposeAsync();
    }
}
