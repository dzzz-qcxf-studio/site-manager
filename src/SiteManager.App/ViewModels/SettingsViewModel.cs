using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiteManager.Core.Configuration;

namespace SiteManager.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IServerProfileStore _settingsStore;
    private readonly IConnectionTester _connectionTester;
    private string _host;
    private int _sshPort;
    private string _username;
    private string _privateKeyPath;
    private string _hostKeySha256;
    private string _publicBaseUrl;
    private int _trashRetentionDays;
    private bool _isBusy;
    private string _statusMessage = string.Empty;

    public SettingsViewModel(IServerProfileStore settingsStore, IConnectionTester connectionTester, ServerProfile initialProfile)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _connectionTester = connectionTester ?? throw new ArgumentNullException(nameof(connectionTester));
        ArgumentNullException.ThrowIfNull(initialProfile);
        _host = initialProfile.Host;
        _sshPort = initialProfile.SshPort;
        _username = initialProfile.Username;
        _privateKeyPath = initialProfile.PrivateKeyPath;
        _hostKeySha256 = initialProfile.HostKeySha256;
        _publicBaseUrl = initialProfile.PublicBaseUrl;
        _trashRetentionDays = initialProfile.TrashRetentionDays;
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanRunCommand);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanRunCommand);
    }

    public string Host { get => _host; set => SetDraft(ref _host, value); }

    public int SshPort { get => _sshPort; set => SetDraft(ref _sshPort, value); }

    public string Username { get => _username; set => SetDraft(ref _username, value); }

    public string PrivateKeyPath { get => _privateKeyPath; set => SetDraft(ref _privateKeyPath, value); }

    public string HostKeySha256 { get => _hostKeySha256; set => SetDraft(ref _hostKeySha256, value); }

    public string PublicBaseUrl { get => _publicBaseUrl; set => SetDraft(ref _publicBaseUrl, value); }

    public int TrashRetentionDays { get => _trashRetentionDays; set => SetDraft(ref _trashRetentionDays, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommandAvailability();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsValid => TryBuildProfile(out _);

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand TestConnectionCommand { get; }

    private async Task SaveAsync()
    {
        if (!TryBuildProfile(out var profile))
        {
            return;
        }

        await ExecuteAsync(async cancellationToken =>
        {
            await _settingsStore.SaveAsync(profile, cancellationToken);
            StatusMessage = "设置已保存。重启应用后，发布与同步将使用这台服务器。";
        });
    }

    private async Task TestConnectionAsync()
    {
        if (!TryBuildProfile(out var profile))
        {
            return;
        }

        await ExecuteAsync(async cancellationToken =>
        {
            var status = await _connectionTester.TestAsync(profile, cancellationToken);
            StatusMessage = $"连接成功：服务器时间 {status.ServerTime.LocalDateTime:yyyy-MM-dd HH:mm}，可用空间 {status.FreeBytes:N0} 字节。";
        });
    }

    private bool CanRunCommand() => !IsBusy && TryBuildProfile(out _);

    private bool TryBuildProfile(out ServerProfile profile)
    {
        try
        {
            profile = new ServerProfile(Host, SshPort, Username, PrivateKeyPath, HostKeySha256, PublicBaseUrl, TrashRetentionDays);
            profile.Validate();
            return true;
        }
        catch (ArgumentException)
        {
            profile = null!;
            return false;
        }
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> operation)
    {
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            await operation(CancellationToken.None);
        }
        catch (Exception exception)
        {
            StatusMessage = $"操作失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetDraft<T>(ref T field, T value)
    {
        if (SetProperty(ref field, value))
        {
            OnPropertyChanged(nameof(IsValid));
            NotifyCommandAvailability();
        }
    }

    private void NotifyCommandAvailability()
    {
        SaveCommand?.NotifyCanExecuteChanged();
        TestConnectionCommand?.NotifyCanExecuteChanged();
    }
}
