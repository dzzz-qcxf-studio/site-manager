using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using SiteManager.Core.Models;
using SiteManager.Core.Configuration;
using SiteManager.Core.Publishing;

namespace SiteManager.App.ViewModels;

public sealed class DefaultArchivePathFactory : IArchivePathFactory
{
    public string CreatePath(Guid requestId)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SiteManager",
            "archives");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{requestId:N}.tar.gz");
    }
}

public sealed class DefaultSiteLinkService(string publicBaseUrl = "http://47.86.89.203/s") : ISiteLinkService
{
    public string Build(SiteManifest site) => site.BuildPublicUrl(publicBaseUrl);
}

internal interface IClipboardBackend
{
    void SetText(string text);

    bool TryGetText(out string? text);
}

internal sealed class WpfClipboardBackend : IClipboardBackend
{
    public void SetText(string text) => Clipboard.SetText(text);

    public bool TryGetText(out string? text)
    {
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            return text is not null;
        }
        catch (ExternalException)
        {
            text = null;
            return false;
        }
    }
}

public sealed class WpfClipboardService : IClipboardService
{
    private const int ClipboardBusyHResult = unchecked((int)0x800401D0); // CLIPBRD_E_CANT_OPEN
    private const int ClipboardRetryCount = 5;
    private readonly IClipboardBackend _clipboard;

    public WpfClipboardService()
        : this(new WpfClipboardBackend())
    {
    }

    internal WpfClipboardService(IClipboardBackend clipboard)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Keep this call on WPF's STA dispatcher thread. ConfigureAwait(false)
                // would resume on a worker thread and make the next clipboard call fail.
                _clipboard.SetText(text);
                return;
            }
            catch (ExternalException exception) when (exception.HResult == ClipboardBusyHResult)
            {
                if (_clipboard.TryGetText(out var clipboardText) && string.Equals(clipboardText, text, StringComparison.Ordinal))
                {
                    return;
                }

                if (attempt >= ClipboardRetryCount)
                {
                    throw;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(80 * (attempt + 1)), cancellationToken);
            }
        }
    }
}

public sealed class SystemBrowserService : IBrowserService
{
    public Task OpenAsync(Uri address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!address.IsAbsoluteUri || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Only HTTP(S) site links can be opened.", nameof(address));
        }

        Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}

public sealed class WpfConfirmationService : IConfirmationService
{
    public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
    }
}

public sealed class RemoteConnectionTester(IRemotePublisherFactory remotePublisherFactory) : IConnectionTester
{
    public Task<RemoteServerStatus> TestAsync(ServerProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return remotePublisherFactory.Create(profile).GetStatusAsync(cancellationToken);
    }
}
