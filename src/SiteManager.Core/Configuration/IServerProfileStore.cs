namespace SiteManager.Core.Configuration;

public interface IServerProfileStore
{
    Task<ServerProfile?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ServerProfile profile, CancellationToken cancellationToken);
}
