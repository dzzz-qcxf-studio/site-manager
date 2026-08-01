namespace SiteManager.Core.Transfers;

public sealed class ResumableUploadEngine
{
    private const int BufferSize = 1024 * 1024;

    public async Task UploadAsync(
        Stream source,
        IRemoteUploadStream remote,
        IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(remote);
        if (!source.CanRead || !source.CanSeek)
        {
            throw new ArgumentException("Upload source must be readable and seekable.", nameof(source));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var remoteOffset = await remote.GetLengthAsync(cancellationToken);
        if (remoteOffset < 0 || remoteOffset > source.Length)
        {
            throw new InvalidDataException("Remote partial exceeds source length.");
        }

        source.Position = remoteOffset;
        await remote.SeekAsync(remoteOffset, cancellationToken);

        var completed = remoteOffset;
        var buffer = new byte[BufferSize];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await remote.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            completed += read;
            progress?.Report(new UploadProgress(completed, source.Length));
        }

        await remote.FlushAsync(cancellationToken);
    }
}
