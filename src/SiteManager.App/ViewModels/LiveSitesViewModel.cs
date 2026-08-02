using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiteManager.Core.Models;
using SiteManager.Core.Publishing;

namespace SiteManager.App.ViewModels;

public sealed class LiveSitesViewModel : ObservableObject
{
    private readonly ISiteSyncService _siteSyncService;
    private readonly IRemotePublisher _remotePublisher;
    private readonly IClipboardService _clipboardService;
    private readonly IBrowserService _browserService;
    private readonly ISiteLinkService _linkService;
    private readonly SiteCatalogState? _catalog;
    private readonly ObservableCollection<SiteManifest> _sites = [];
    private string _searchText = string.Empty;
    private SiteManifest? _selectedSite;
    private bool _isLoading;
    private string? _errorMessage;

    public LiveSitesViewModel(
        ISiteSyncService siteSyncService,
        IRemotePublisher remotePublisher,
        IClipboardService clipboardService,
        IBrowserService browserService,
        ISiteLinkService linkService,
        SiteCatalogState? catalog = null)
    {
        _siteSyncService = siteSyncService ?? throw new ArgumentNullException(nameof(siteSyncService));
        _remotePublisher = remotePublisher ?? throw new ArgumentNullException(nameof(remotePublisher));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _browserService = browserService ?? throw new ArgumentNullException(nameof(browserService));
        _linkService = linkService ?? throw new ArgumentNullException(nameof(linkService));
        _catalog = catalog;
        if (_catalog is not null)
        {
            _catalog.Changed += ApplySites;
        }
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);
        CopyLinkCommand = new AsyncRelayCommand(CopyLinkAsync, () => SelectedSite is not null);
        OpenLinkCommand = new AsyncRelayCommand(OpenLinkAsync, () => SelectedSite is not null);
        UpdateCommand = new RelayCommand(RequestUpdate, () => SelectedSite is not null);
        MoveToTrashCommand = new AsyncRelayCommand(MoveToTrashAsync, () => SelectedSite is not null && !IsLoading);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(FilteredSites));
            }
        }
    }

    public SiteManifest? SelectedSite
    {
        get => _selectedSite;
        set
        {
            if (SetProperty(ref _selectedSite, value))
            {
                CopyLinkCommand.NotifyCanExecuteChanged();
                OpenLinkCommand.NotifyCanExecuteChanged();
                UpdateCommand.NotifyCanExecuteChanged();
                MoveToTrashCommand.NotifyCanExecuteChanged();
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
                MoveToTrashCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public IReadOnlyList<SiteManifest> Sites => _sites;

    public IReadOnlyList<SiteManifest> FilteredSites => _sites
        .Where(MatchesSearch)
        .OrderByDescending(site => site.UpdatedAt)
        .ToArray();

    public bool IsEmpty => _sites.Count == 0 && !IsLoading && string.IsNullOrWhiteSpace(ErrorMessage);

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand CopyLinkCommand { get; }

    public IAsyncRelayCommand OpenLinkCommand { get; }

    public IRelayCommand UpdateCommand { get; }

    public IAsyncRelayCommand MoveToTrashCommand { get; }

    public event Action<SiteManifest>? UpdateRequested;

    public Task StartInitialSyncAsync() => RefreshCommand.ExecuteAsync(null);

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
        ArgumentNullException.ThrowIfNull(sites);
        _sites.Clear();
        foreach (var site in sites.Where(site => site.Status == SiteStatus.Live).OrderByDescending(site => site.UpdatedAt))
        {
            _sites.Add(site);
        }

        if (SelectedSite is not null && _sites.All(site => site.Id != SelectedSite.Id))
        {
            SelectedSite = null;
        }

        OnPropertyChanged(nameof(Sites));
        OnPropertyChanged(nameof(FilteredSites));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task RefreshAsync()
    {
        await ExecuteLoadingAsync(async cancellationToken =>
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

    private async Task CopyLinkAsync()
    {
        if (SelectedSite is null)
        {
            return;
        }

        ErrorMessage = null;
        try
        {
            await _clipboardService.SetTextAsync(_linkService.Build(SelectedSite), CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Clipboard providers can briefly fail when another process owns the
            // clipboard. Surface the failure in the page instead of letting an
            // AsyncRelayCommand exception terminate the WPF process.
            ErrorMessage = $"复制链接失败：{exception.Message}";
        }
    }

    private async Task OpenLinkAsync()
    {
        if (SelectedSite is not null)
        {
            await _browserService.OpenAsync(new Uri(_linkService.Build(SelectedSite), UriKind.Absolute), CancellationToken.None);
        }
    }

    private void RequestUpdate()
    {
        if (SelectedSite is not null)
        {
            UpdateRequested?.Invoke(SelectedSite);
        }
    }

    private async Task MoveToTrashAsync()
    {
        if (SelectedSite is null)
        {
            return;
        }

        var selected = SelectedSite;
        await ExecuteLoadingAsync(async cancellationToken =>
        {
            async Task Mutate(CancellationToken token)
            {
                EnsureLiveSelection(selected.Id);
                await _remotePublisher.TrashAsync(Guid.NewGuid(), selected.Id, token);
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

    private void EnsureLiveSelection(Guid siteId)
    {
        if (_catalog is not null && _catalog.Sites.All(site => site.Id != siteId || site.Status != SiteStatus.Live))
        {
            throw new InvalidOperationException("网站状态已变化，请先同步已上架网站后再操作。");
        }
    }

    private bool MatchesSearch(SiteManifest site)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var query = SearchText.Trim();
        return site.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || site.Note.Contains(query, StringComparison.OrdinalIgnoreCase)
            || _linkService.Build(site).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ExecuteLoadingAsync(Func<CancellationToken, Task> operation)
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
