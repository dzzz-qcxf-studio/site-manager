using SiteManager.Core.Transfers;

namespace SiteManager.Core.Tests.Transfers;

public sealed class ResumableUploadEngineTests
{
    [Fact]
    public async Task UploadAsync_starts_at_remote_offset()
    {
        await using var source = new MemoryStream("0123456789"u8.ToArray());
        await using var remote = new FakeRemoteUploadStream("0123"u8.ToArray());

        await new ResumableUploadEngine().UploadAsync(source, remote, null, TestContext.Current.CancellationToken);

        Assert.Equal("0123456789"u8.ToArray(), remote.GetContents());
        Assert.Equal(4, remote.FirstSeekOffset);
    }

    [Fact]
    public async Task UploadAsync_reports_monotonic_byte_progress()
    {
        await using var source = new MemoryStream(new byte[1_200_000]);
        await using var remote = new FakeRemoteUploadStream([]);
        var reports = new List<UploadProgress>();

        await new ResumableUploadEngine().UploadAsync(
            source,
            remote,
            new InlineProgress<UploadProgress>(reports.Add),
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(reports);
        Assert.Equal(source.Length, reports[^1].CompletedBytes);
        Assert.All(reports, report => Assert.Equal(source.Length, report.TotalBytes));
        Assert.True(reports.Zip(reports.Skip(1), (left, right) => right.CompletedBytes >= left.CompletedBytes).All(value => value));
    }

    [Fact]
    public async Task UploadAsync_rejects_remote_offset_larger_than_source()
    {
        await using var source = new MemoryStream("0123"u8.ToArray());
        await using var remote = new FakeRemoteUploadStream("01234"u8.ToArray());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ResumableUploadEngine().UploadAsync(source, remote, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UploadAsync_stops_on_cancellation_without_truncating_remote_partial()
    {
        await using var source = new MemoryStream(new byte[1_200_000]);
        await using var remote = new FakeRemoteUploadStream("0123"u8.ToArray());
        using var cancellation = new CancellationTokenSource();
        remote.OnWrite = () => cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ResumableUploadEngine().UploadAsync(source, remote, null, cancellation.Token));

        Assert.True(remote.Length >= 4);
        Assert.Equal("0123"u8.ToArray(), remote.GetContents()[..4]);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class FakeRemoteUploadStream : IRemoteUploadStream
    {
        private readonly MemoryStream _contents = new();

        public FakeRemoteUploadStream(byte[] initialContents)
        {
            _contents.Write(initialContents);
            _contents.Position = 0;
        }

        public Action? OnWrite { get; set; }

        public long FirstSeekOffset { get; private set; } = -1;

        public long Length => _contents.Length;

        public Task<long> GetLengthAsync(CancellationToken cancellationToken) => Task.FromResult(_contents.Length);

        public Task SeekAsync(long offset, CancellationToken cancellationToken)
        {
            FirstSeekOffset = offset;
            _contents.Position = offset;
            return Task.CompletedTask;
        }

        public Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            _contents.Write(buffer.Span);
            OnWrite?.Invoke();
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public byte[] GetContents() => _contents.ToArray();

        public ValueTask DisposeAsync() => _contents.DisposeAsync();
    }
}
