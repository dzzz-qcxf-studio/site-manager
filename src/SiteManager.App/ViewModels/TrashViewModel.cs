using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiteManager.Core.Models;
using SiteManager.Core.Publishing;

namespace SiteManager.App.ViewModels;

public sealed class TrashViewModel : ObservableObject
{
    private readonly ISiteSyncService _siteSyncService;
    private readonly IRemotePublisher _remotePublisher;
    private readonly IConfirmationService _confirmationService;
    private readonly SiteCatalogState? _catalog;
    private SiteManifest? _selectedTrashSite;
    private bool _isLoading;
    private string? _errorMessage;

    public TrashViewModel(
        ISiteSyncService siteSyncService,
        IRemotePublisher remotePublisher,
        IConfirmationService confirmationService,
        SiteCatalogState? catalog = null)
    {
        _siteSyncService = siteSyncService ?? throw new ArgumentNullException(nameof(siteSyncService));
        _remotePublisher = remotePublisher ?? throw new ArgumentNullException(nameof(remotePublisher));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _catalog = catalog;
        if (_catalog is not null)
        {
            _catalog.Changed += ApplySites;
        }
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => SelectedTrashSite is not null && !IsLoading);
        PurgeCommand = new AsyncRelayCommand(PurgeAsync, () => SelectedTrashSite is not null && !IsLoading);
    }

    public ObservableCollection<SiteManifest> LiveSites { get; } = [];

    public ObservableCollection<SiteManifest> TrashSites { get; } = [];

    public SiteManifest? SelectedTrashSite
    {
        get => _selectedTrashSite;
        set
        {
            if (SetProperty(ref _selectedTrashSite, value))
            {
                RestoreCommand.NotifyCanExecuteChanged();
                PurgeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
                RestoreCommand.NotifyCanExecuteChanged();
                PurgeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand RestoreCommand { get; }

    public IAsyncRelayCommand PurgeCommand { get; }

    private async Task RefreshAsync()
    {
        await ExecuteAsync(async cancellationToken =>
        {
            if (_catalog is null)
            {
                ApplySites(await _siteSyncService.SyncAsync(cancellationToken));
            }
            else
            {
                await _catalog.SyncAndReplaceAsync(_siteSyncService, cancellationToken);
            }
        });
    }

    private async Task RestoreAsync()
    {
        if (SelectedTrashSite is null)
        {
            return;
        }

        var selected = SelectedTrashSite;
        await ExecuteAsync(async cancellationToken =>
        {
            async Task Mutate(CancellationToken token)
            {
                EnsureTrashSelection(selected.Id);
                await _remotePublisher.RestoreAsync(Guid.NewGuid(), selected.Id, token);
            }

            if (_catalog is null)
            {
                await Mutate(cancellationToken);
                ApplySites(await _siteSyncService.SyncAsync(cancellationToken));
            }
            else
            {
                await _catalog.MutateAndSyncAsync(_siteSyncService, Mutate, cancellationToken);
            }
        });
    }

    private async Task PurgeAsync()
    {
        if (SelectedTrashSite is null)
        {
            return;
        }

        var selected = SelectedTrashSite;
        var confirmed = await _confirmationService.ConfirmAsync(
            "永久删除网站",
            $"将永久删除“{selected.Name}”，此操作无法恢复。",
            CancellationToken.None);
        if (!confirmed)
        {
            return;
        }

        await ExecuteAsync(async cancellationToken =>
        {
            async Task Mutate(CancellationToken token)
            {
                EnsureTrashSelection(selected.Id);
                await _remotePublisher.PurgeAsync(Guid.NewGuid(), selected.Id, token);
            }

            if (_catalog is null)
            {
                await Mutate(cancellationToken);
                ApplySites(await _siteSyncService.SyncAsync(cancellationToken));
            }
            else
            {
                await _catalog.MutateAndSyncAsync(_siteSyncService, Mutate, cancellationToken);
            }
        });
    }

    public void ReplaceSites(IEnumerable<SiteManifest> sites)
    {
        ArgumentNullException.ThrowIfNull(sites);
        if (_catalog is not null)
        {
            _catalog.ReplaceSites(sites);
            return;
        }

        ApplySites(sites);
    }

    private void ApplySites(IEnumerable<SiteManifest> sites)
    {
        LiveSites.Clear();
        TrashSites.Clear();
        foreach (var site in sites.OrderByDescending(site => site.UpdatedAt))
        {
            if (site.Status == SiteStatus.Live)
            {
                LiveSites.Add(site);
            }
            else
            {
                TrashSites.Add(site);
            }
        }

        if (SelectedTrashSite is not null && TrashSites.All(site => site.Id != SelectedTrashSite.Id))
        {
            SelectedTrashSite = null;
        }
    }

    private void EnsureTrashSelection(Guid siteId)
    {
        if (_catalog is not null && _catalog.Sites.All(site => site.Id != siteId || site.Status != SiteStatus.Trash))
        {
            throw new InvalidOperationException("回收站项目状态已变化，请先同步回收站后再操作。");
        }
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> operation)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await operation(CancellationToken.None);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
