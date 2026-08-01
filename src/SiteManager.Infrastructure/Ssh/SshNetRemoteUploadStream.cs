using System.Security;
using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using SiteManager.Core.Configuration;
using SiteManager.Core.Transfers;

namespace SiteManager.Infrastructure.Ssh;

public sealed class SshNetRemoteUploadStream : IRemoteUploadStream
{
    private readonly SftpClient _client;
    private readonly string _remotePath;
    private Stream? _stream;

    private SshNetRemoteUploadStream(SftpClient client, string remotePath)
    {
        _client = client;
        _remotePath = remotePath;
    }

    public static async Task<SshNetRemoteUploadStream> ConnectAsync(
        ServerProfile profile,
        string remotePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(remotePath);
        profile.Validate();

        var keyFile = new PrivateKeyFile(profile.PrivateKeyPath);
        var authentication = new PrivateKeyAuthenticationMethod(profile.Username, keyFile);
        var connection = new ConnectionInfo(profile.Host, profile.SshPort, profile.Username, authentication);
        var client = new SftpClient(connection);
        var hostKeyReceived = false;
        client.HostKeyReceived += (_, eventArgs) =>
        {
            hostKeyReceived = true;
            eventArgs.CanTrust = IsExpectedHostKeyFingerprint(eventArgs.FingerPrintSHA256, profile.HostKeySha256);
        };

        try
        {
            await client.ConnectAsync(cancellationToken);
            if (!hostKeyReceived)
            {
                throw new SecurityException("SSH connection completed without a host key fingerprint.");
            }

            return new SshNetRemoteUploadStream(client, remotePath);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public static bool IsExpectedHostKeyFingerprint(string observedBase64Sha256, string expectedOpenSshFingerprint)
    {
        ArgumentNullException.ThrowIfNull(observedBase64Sha256);
        ArgumentNullException.ThrowIfNull(expectedOpenSshFingerprint);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes($"SHA256:{observedBase64Sha256}"),
            Encoding.ASCII.GetBytes(expectedOpenSshFingerprint));
    }

    public async Task<long> GetLengthAsync(CancellationToken cancellationToken)
    {
        if (!await _client.ExistsAsync(_remotePath, cancellationToken))
        {
            return 0;
        }

        return checked((long)(await _client.GetAttributesAsync(_remotePath, cancellationToken)).Size);
    }

    public async Task SeekAsync(long offset, CancellationToken cancellationToken)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }

        _stream = await _client.OpenAsync(_remotePath, FileMode.OpenOrCreate, FileAccess.Write, cancellationToken);
        _stream.Seek(offset, SeekOrigin.Begin);
    }

    public Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
        GetStream().WriteAsync(buffer, cancellationToken).AsTask();

    public Task FlushAsync(CancellationToken cancellationToken) =>
        GetStream().FlushAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
            _stream = null;
        }

        _client.Dispose();
    }

    private Stream GetStream() => _stream ?? throw new InvalidOperationException("Remote upload stream has not been positioned.");
}
