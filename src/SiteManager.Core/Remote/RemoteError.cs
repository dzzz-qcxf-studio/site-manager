namespace SiteManager.Core.Remote;

public sealed record RemoteError(string Code, string Message, bool Retryable);
