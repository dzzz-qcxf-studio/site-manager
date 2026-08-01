namespace SiteManager.Core.Transfers;

public interface IRemoteUploadStream : IAsyncDisposable
{
    Task<long> GetLengthAsync(CancellationToken cancellationToken);

    Task SeekAsync(long offset, CancellationToken cancellationToken);

    Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

    Task FlushAsync(CancellationToken cancellationToken);
}
