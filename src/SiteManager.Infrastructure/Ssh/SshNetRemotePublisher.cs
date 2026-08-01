using System.Security;
using System.Text;
using System.Text.Json;
using Renci.SshNet;
using SiteManager.Core.Configuration;
using SiteManager.Core.Models;
using SiteManager.Core.Publishing;
using SiteManager.Core.Remote;
using SiteManager.Core.Transfers;

namespace SiteManager.Infrastructure.Ssh;

public sealed class SshNetRemotePublisherFactory : IRemotePublisherFactory
{
    public IRemotePublisher Create(ServerProfile profile) => new SshNetRemotePublisher(profile);
}

public sealed class SshNetRemotePublisher : IRemotePublisher
{
    private readonly ServerProfile _profile;

    public SshNetRemotePublisher(ServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        _profile = profile;
    }

    public async Task<RemoteServerStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var data = await ExecuteAsync<StatusData>(BuildStatusCommand(Guid.NewGuid()), cancellationToken);
        return new RemoteServerStatus(data.ServerTime, data.Disk.TotalBytes, data.Disk.FreeBytes);
    }

    public async Task<RemoteUploadSession> PrepareAsync(RemotePrepareRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePrepareRequest(request);
        var data = await ExecuteAsync<PrepareData>(BuildPrepareCommand(request), cancellationToken);
        return new RemoteUploadSession(data.UploadId, data.RemotePath, data.ResumeOffset, data.ExpiresAt);
    }

    public async Task<IRemoteUploadStream> OpenUploadStreamAsync(RemoteUploadSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        return await SshNetRemoteUploadStream.ConnectAsync(_profile, session.RemotePath, cancellationToken);
    }

    public async Task<SiteManifest> PublishAsync(RemotePublishRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var data = await ExecuteAsync<SiteData>(BuildPublishCommand(request), cancellationToken);
        return data.ToManifest();
    }

    public static string BuildPublishCommand(Guid requestId, Guid uploadId, string name, string note) =>
        BuildPublishCommand(new RemotePublishRequest(requestId, uploadId, null, name, note));

    public async Task<IReadOnlyList<SiteManifest>> ListAsync(SiteStatus? status, CancellationToken cancellationToken)
    {
        var filter = status switch
        {
            SiteStatus.Live => "live",
            SiteStatus.Trash => "trash",
            null => "all",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
        var data = await ExecuteAsync<ListData>(
            $"site-managerctl list --request-id {FormatGuid(Guid.NewGuid())} --status {filter}",
            cancellationToken);
        return data.Sites.Select(site => site.ToManifest()).ToArray();
    }

    public async Task<SiteManifest> TrashAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) =>
        (await ExecuteAsync<SiteData>(BuildSiteCommand("trash", requestId, siteId), cancellationToken)).ToManifest();

    public async Task<SiteManifest> RestoreAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken) =>
        (await ExecuteAsync<SiteData>(BuildSiteCommand("restore", requestId, siteId), cancellationToken)).ToManifest();

    public async Task PurgeAsync(Guid requestId, Guid siteId, CancellationToken cancellationToken)
    {
        await ExecuteAsync<JsonElement>(BuildSiteCommand("purge", requestId, siteId), cancellationToken);
    }

    public static string BuildStatusCommand(Guid requestId) =>
        $"site-managerctl status --request-id {FormatGuid(requestId)}";

    private async Task<T> ExecuteAsync<T>(string commandText, CancellationToken cancellationToken)
    {
        _profile.Validate();
        var client = new SshClient(CreateConnectionInfo(_profile));
        var hostKeyReceived = false;
        client.HostKeyReceived += (_, eventArgs) =>
        {
            hostKeyReceived = true;
            eventArgs.CanTrust = SshNetRemoteUploadStream.IsExpectedHostKeyFingerprint(
                eventArgs.FingerPrintSHA256,
                _profile.HostKeySha256);
        };

        try
        {
            await client.ConnectAsync(cancellationToken);
            if (!hostKeyReceived)
            {
                throw new SecurityException("SSH connection completed without a host key fingerprint.");
            }

            using var command = client.CreateCommand(commandText, Encoding.UTF8);
            await command.ExecuteAsync(cancellationToken);
            var response = command.Result.Trim();
            if (string.IsNullOrEmpty(response))
            {
                throw new IOException($"Remote command did not return a protocol response: {command.Error}");
            }

            return RemoteProtocol.Parse<T>(response);
        }
        finally
        {
            client.Dispose();
        }
    }

    private static ConnectionInfo CreateConnectionInfo(ServerProfile profile)
    {
        var privateKey = new PrivateKeyFile(profile.PrivateKeyPath);
        var authentication = new PrivateKeyAuthenticationMethod(profile.Username, privateKey);
        return new ConnectionInfo(profile.Host, profile.SshPort, profile.Username, authentication);
    }

    private static string BuildPublishCommand(RemotePublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var encodedName = FormatEncodedArgument(RemoteProtocol.EncodeText(request.Name));
        var encodedNote = FormatEncodedArgument(RemoteProtocol.EncodeText(request.Note));
        return $"site-managerctl publish --request-id {FormatGuid(request.RequestId)} --upload-id {FormatGuid(request.UploadId)} --name-b64 {encodedName} --note-b64 {encodedNote}";
    }

    private static string FormatEncodedArgument(string encodedText) => encodedText.Length == 0 ? "\"\"" : encodedText;

    private static string BuildPrepareCommand(RemotePrepareRequest request)
    {
        var siteArgument = request.ExistingSiteId is { } siteId ? $" --site-id {FormatGuid(siteId)}" : string.Empty;
        var mode = request.IsUpdate ? "update" : "create";
        return $"site-managerctl prepare --request-id {FormatGuid(request.RequestId)} --mode {mode}{siteArgument} --size {request.ArchiveBytes} --sha256 {request.ExpectedSha256}";
    }

    private static string BuildSiteCommand(string action, Guid requestId, Guid siteId) =>
        $"site-managerctl {action} --request-id {FormatGuid(requestId)} --site-id {FormatGuid(siteId)}";

    private static string FormatGuid(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty UUID is required.", nameof(value));
        }

        return value.ToString("D");
    }

    private static void ValidatePrepareRequest(RemotePrepareRequest request)
    {
        _ = FormatGuid(request.RequestId);
        if (request.ArchiveBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ArchiveBytes));
        }

        if (request.ExpectedSha256.Length != 64 || request.ExpectedSha256.Any(character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            throw new ArgumentException("Archive hash must be lowercase SHA-256.", nameof(request.ExpectedSha256));
        }

        if (request.IsUpdate)
        {
            _ = FormatGuid(request.ExistingSiteId!.Value);
        }
    }

    private sealed class StatusData
    {
        public DateTimeOffset ServerTime { get; init; }

        public required DiskData Disk { get; init; }
    }

    private sealed class DiskData
    {
        public long TotalBytes { get; init; }

        public long FreeBytes { get; init; }
    }

    private sealed class PrepareData
    {
        public Guid UploadId { get; init; }

        public required string RemotePath { get; init; }

        public long ResumeOffset { get; init; }

        public DateTimeOffset ExpiresAt { get; init; }
    }

    private sealed class ListData
    {
        public required List<SiteData> Sites { get; init; }
    }

    private sealed class SiteData
    {
        public Guid Id { get; init; }

        public required string Name { get; init; }

        public required string Note { get; init; }

        public required string Slug { get; init; }

        public required string Status { get; init; }

        public int Version { get; init; }

        public long SizeBytes { get; init; }

        public required string ContentSha256 { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public DateTimeOffset? TrashedAt { get; init; }

        public DateTimeOffset? PurgeAt { get; init; }

        public SiteManifest ToManifest() => new(
            Id, Name, Note, Slug,
            Status switch
            {
                "live" => SiteStatus.Live,
                "trash" => SiteStatus.Trash,
                _ => throw new InvalidDataException($"Remote site status was invalid: {Status}")
            },
            Version, SizeBytes, ContentSha256, CreatedAt, UpdatedAt, TrashedAt, PurgeAt);
    }
}
