using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SiteManager.Core.Publishing;
using SiteManager.Core.Storage;
using SiteManager.Core.Transfers;

namespace SiteManager.App.ViewModels;

public sealed class TransferCenterViewModel : ObservableObject, ITransferProgressSink
{
    private readonly ITransferHistoryStore _historyStore;
    private readonly ObservableCollection<TransferHistoryEntry> _history = [];
    private string _name = "暂无传输任务";
    private PublishStage? _stage;
    private long _completedBytes;
    private long _totalBytes;
    private Guid _requestId;
    private string _sourceFolderPath = string.Empty;
    private DateTimeOffset _startedAt;
    private bool _historyRecorded;

    public TransferCenterViewModel(ITransferHistoryStore? historyStore = null)
    {
        _historyStore = historyStore ?? new InMemoryTransferHistoryStore();
    }

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public PublishStage? Stage
    {
        get => _stage;
        private set => SetProperty(ref _stage, value);
    }

    public long CompletedBytes
    {
        get => _completedBytes;
        private set
        {
            if (SetProperty(ref _completedBytes, value))
            {
                OnPropertyChanged(nameof(ProgressPercent));
            }
        }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        private set
        {
            if (SetProperty(ref _totalBytes, value))
            {
                OnPropertyChanged(nameof(ProgressPercent));
            }
        }
    }

    public double ProgressPercent => TotalBytes == 0 ? 0 : Math.Clamp((double)CompletedBytes / TotalBytes * 100, 0, 100);

    public bool IsActive => Stage is PublishStage.Scanning or PublishStage.Archiving or PublishStage.Preparing or PublishStage.Uploading or PublishStage.Verifying or PublishStage.Publishing;

    public IReadOnlyList<TransferHistoryEntry> History => _history;

    public bool HasHistory => _history.Count > 0;

    public bool HasNoHistory => !HasHistory;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var entries = await _historyStore.GetAsync(cancellationToken);
        _history.Clear();
        foreach (var entry in entries.OrderByDescending(entry => entry.CompletedAt))
        {
            _history.Add(entry);
        }

        OnPropertyChanged(nameof(History));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HasNoHistory));
    }

    public void Begin(string name, Guid? requestId = null, string? sourceFolderPath = null)
    {
        Name = name;
        Stage = PublishStage.Scanning;
        CompletedBytes = 0;
        TotalBytes = 0;
        _requestId = requestId ?? Guid.NewGuid();
        _sourceFolderPath = sourceFolderPath ?? string.Empty;
        _startedAt = DateTimeOffset.UtcNow;
        _historyRecorded = false;
        OnPropertyChanged(nameof(IsActive));
    }

    public void Report(PublishProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        Stage = progress.Stage;
        CompletedBytes = progress.CompletedBytes;
        TotalBytes = progress.TotalBytes;
        OnPropertyChanged(nameof(IsActive));
        if (!_historyRecorded && progress.Stage is PublishStage.Completed or PublishStage.Failed or PublishStage.Cancelled)
        {
            _historyRecorded = true;
            var entry = new TransferHistoryEntry(
                _requestId,
                Name,
                _sourceFolderPath,
                progress.Stage,
                _startedAt,
                DateTimeOffset.UtcNow,
                progress.CompletedBytes,
                progress.TotalBytes);
            _history.Insert(0, entry);
            OnPropertyChanged(nameof(History));
            OnPropertyChanged(nameof(HasHistory));
            OnPropertyChanged(nameof(HasNoHistory));
            _ = PersistHistoryAsync(entry);
        }
    }

    private async Task PersistHistoryAsync(TransferHistoryEntry entry)
    {
        try
        {
            await _historyStore.AppendAsync(entry, CancellationToken.None);
        }
        catch
        {
            // History is auxiliary UI state; a local write failure must not
            // change the result of the remote publish operation.
        }
    }
}

internal sealed class InMemoryTransferHistoryStore : ITransferHistoryStore
{
    private readonly List<TransferHistoryEntry> _entries = [];

    public Task<IReadOnlyList<TransferHistoryEntry>> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TransferHistoryEntry>>(_entries.ToArray());

    public Task AppendAsync(TransferHistoryEntry entry, CancellationToken cancellationToken)
    {
        _entries.Insert(0, entry);
        return Task.CompletedTask;
    }
}
