namespace SiteManager.Core.Remote;

public sealed record RemoteEnvelope<T>(
    int ProtocolVersion,
    bool Ok,
    Guid RequestId,
    T? Data,
    RemoteError? Error);
