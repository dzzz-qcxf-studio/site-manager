using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiteManager.Core.Models;
using SiteManager.Core.Publishing;
using SiteManager.Core.Storage;
using SiteManager.Core.Validation;

namespace SiteManager.App.ViewModels;

public sealed class PublishViewModel : ObservableObject
{
    private readonly IWebsiteFolderValidator _validator;
    private readonly IPublishSiteService _publishSiteService;
    private readonly ITransferProgressSink _transferProgressSink;
    private readonly IArchivePathFactory _archivePathFactory;
    private readonly ISiteFolderPathStore? _siteFolderPathStore;
    private string _folderPath = string.Empty;
    private string _name = string.Empty;
    private string _note = string.Empty;
    private FolderValidationResult? _validationResult;
    private PublishStage? _currentStage;
    private bool _isPublishing;
    private string? _errorMessage;
    private SiteManifest? _publishedSite;
    private Guid? _existingSiteId;
    private CancellationTokenSource? _cancellation;

    public PublishViewModel(
        IWebsiteFolderValidator validator,
        IPublishSiteService publishSiteService,
        ITransferProgressSink transferProgressSink,
        IArchivePathFactory archivePathFactory,
        ISiteFolderPathStore? siteFolderPathStore = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _publishSiteService = publishSiteService ?? throw new ArgumentNullException(nameof(publishSiteService));
        _transferProgressSink = transferProgressSink ?? throw new ArgumentNullException(nameof(transferProgressSink));
        _archivePathFactory = archivePathFactory ?? throw new ArgumentNullException(nameof(archivePathFactory));
        _siteFolderPathStore = siteFolderPathStore;
        ValidateFolderCommand = new RelayCommand(ValidateFolder);
        PublishCommand = new AsyncRelayCommand(PublishAsync, () => CanPublish);
        CancelCommand = new RelayCommand(Cancel, () => IsPublishing);
        NewSiteCommand = new RelayCommand(BeginNew, () => !IsPublishing);
    }

    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (SetProperty(ref _folderPath, value ?? string.Empty))
            {
                ValidationResult = null;
                UpdatePublishAvailability();
            }
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                UpdatePublishAvailability();
            }
        }
    }

    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value ?? string.Empty);
    }

    public FolderValidationResult? ValidationResult
    {
        get => _validationResult;
        private set
        {
            if (SetProperty(ref _validationResult, value))
            {
                OnPropertyChanged(nameof(IsFolderValid));
                UpdatePublishAvailability();
            }
        }
    }

    public bool IsFolderValid => ValidationResult?.IsValid == true;

    public PublishStage? CurrentStage
    {
        get => _currentStage;
        private set => SetProperty(ref _currentStage, value);
    }

    public bool IsPublishing
    {
        get => _isPublishing;
        private set
        {
            if (SetProperty(ref _isPublishing, value))
            {
                UpdatePublishAvailability();
                CancelCommand.NotifyCanExecuteChanged();
                NewSiteCommand.NotifyCanExecuteChanged();
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
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public SiteManifest? PublishedSite
    {
        get => _publishedSite;
        private set => SetProperty(ref _publishedSite, value);
    }

    public Guid? ExistingSiteId
    {
        get => _existingSiteId;
        private set
        {
            if (SetProperty(ref _existingSiteId, value))
            {
                OnPropertyChanged(nameof(OperationTitle));
            }
        }
    }

    public string OperationTitle => ExistingSiteId is null ? "上架新网站" : "更新已有网站";

    public bool CanPublish => IsFolderValid && !string.IsNullOrWhiteSpace(Name) && !IsPublishing;

    public IAsyncRelayCommand PublishCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand NewSiteCommand { get; }

    public IRelayCommand ValidateFolderCommand { get; }

    public event Action? TransferRequested;

    public void ValidateFolder()
    {
        ErrorMessage = null;
        try
        {
            var result = _validator.Validate(FolderPath);
            ValidationResult = result;
            if (!result.IsValid)
            {
                ErrorMessage = result.Issues.FirstOrDefault(issue => issue.IsError)?.Message
                    ?? "网站文件夹未通过校验。";
            }
        }
        catch (Exception exception)
        {
            ValidationResult = null;
            ErrorMessage = exception.Message;
        }
    }

    public void BeginUpdate(SiteManifest site)
    {
        ArgumentNullException.ThrowIfNull(site);
        ExistingSiteId = site.Id;
        Name = site.Name;
        Note = site.Note;
        FolderPath = _siteFolderPathStore?.Get(site.Id) ?? string.Empty;
        PublishedSite = null;
        ErrorMessage = null;
    }

    public void BeginNew()
    {
        if (IsPublishing)
        {
            return;
        }

        ExistingSiteId = null;
        FolderPath = string.Empty;
        Name = string.Empty;
        Note = string.Empty;
        ValidationResult = null;
        CurrentStage = null;
        PublishedSite = null;
        ErrorMessage = null;
    }

    private async Task PublishAsync()
    {
        if (!CanPublish)
        {
            return;
        }

        var requestId = Guid.NewGuid();
        _cancellation = new CancellationTokenSource();
        IsPublishing = true;
        ErrorMessage = null;
        PublishedSite = null;
        CurrentStage = PublishStage.Scanning;
        _transferProgressSink.Begin(Name, requestId, FolderPath);
        TransferRequested?.Invoke();

        try
        {
            var request = new PublishSiteRequest(
                requestId,
                FolderPath,
                _archivePathFactory.CreatePath(requestId),
                Name.Trim(),
                Note,
                ExistingSiteId);
            if (ExistingSiteId is { } existingSiteId)
            {
                // Preserve the source path before remote work starts. This
                // keeps the next update usable even when this attempt fails
                // or is cancelled after the user selected a new folder.
                await RememberFolderPathAsync(existingSiteId);
            }
            var progress = new PublishProgressReporter(this);
            PublishedSite = await _publishSiteService.PublishAsync(request, progress, _cancellation.Token);
            await RememberFolderPathAsync(PublishedSite.Id);
            CurrentStage = PublishStage.Completed;
            _transferProgressSink.Report(new PublishProgress(PublishStage.Completed));
        }
        catch (OperationCanceledException)
        {
            CurrentStage = PublishStage.Cancelled;
            ErrorMessage = "发布已取消，已上传内容会保留以便续传。";
            _transferProgressSink.Report(new PublishProgress(PublishStage.Cancelled));
        }
        catch (Exception exception)
        {
            CurrentStage = PublishStage.Failed;
            ErrorMessage = exception.Message;
            _transferProgressSink.Report(new PublishProgress(PublishStage.Failed));
        }
        finally
        {
            IsPublishing = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void Cancel() => _cancellation?.Cancel();

    private void UpdatePublishAvailability() => PublishCommand.NotifyCanExecuteChanged();

    private async Task RememberFolderPathAsync(Guid siteId)
    {
        if (_siteFolderPathStore is null)
        {
            return;
        }

        try
        {
            await _siteFolderPathStore.SetAsync(siteId, FolderPath, CancellationToken.None);
        }
        catch
        {
            // Local path history is auxiliary UI state; a write failure must
            // not change the result of the remote publish operation.
        }
    }

    private sealed class PublishProgressReporter(PublishViewModel viewModel) : IProgress<PublishProgress>
    {
        public void Report(PublishProgress value)
        {
            viewModel.CurrentStage = value.Stage;
            viewModel._transferProgressSink.Report(value);
        }
    }
}
