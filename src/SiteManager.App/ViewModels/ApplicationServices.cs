using SiteManager.Core.Models;
using SiteManager.Core.Configuration;
using SiteManager.Core.Publishing;

namespace SiteManager.App.ViewModels;

public interface IArchivePathFactory
{
    string CreatePath(Guid requestId);
}

public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken);
}

public interface IBrowserService
{
    Task OpenAsync(Uri address, CancellationToken cancellationToken);
}

public interface ISiteLinkService
{
    string Build(SiteManifest site);
}

public interface IConfirmationService
{
    Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken);
}

public interface IConnectionTester
{
    Task<RemoteServerStatus> TestAsync(ServerProfile profile, CancellationToken cancellationToken);
}

public interface ITransferProgressSink
{
    void Begin(string name, Guid? requestId = null, string? sourceFolderPath = null);

    void Report(PublishProgress progress);
}
