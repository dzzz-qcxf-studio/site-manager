using SiteManager.Core.Configuration;

namespace SiteManager.Core.Publishing;

public interface IRemotePublisherFactory
{
    IRemotePublisher Create(ServerProfile profile);
}
