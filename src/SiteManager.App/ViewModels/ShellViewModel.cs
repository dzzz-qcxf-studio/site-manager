using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SiteManager.App.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private AppSection _currentSection = AppSection.LiveSites;
    private readonly bool _isServerConfigured;
    private readonly string? _serverHost;

    public ShellViewModel()
        : this(AppPageComposition.Create())
    {
    }

    public ShellViewModel(AppPageModels pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        LiveSites = pages.LiveSites;
        Publish = pages.Publish;
        Transfers = pages.Transfers;
        Trash = pages.Trash;
        Settings = pages.Settings;
        _isServerConfigured = pages.IsServerConfigured;
        _serverHost = pages.ServerHost;
        LiveSites.UpdateRequested += BeginUpdate;
        Publish.TransferRequested += ShowTransfers;
        NavigateCommand = new RelayCommand<AppSection>(Navigate);
    }

    public AppSection CurrentSection
    {
        get => _currentSection;
        private set
        {
            if (SetProperty(ref _currentSection, value))
            {
                OnPropertyChanged(nameof(CurrentTitle));
                OnPropertyChanged(nameof(CurrentDescription));
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(IsLiveSitesSelected));
                OnPropertyChanged(nameof(IsPublishSelected));
                OnPropertyChanged(nameof(IsTransfersSelected));
                OnPropertyChanged(nameof(IsTrashSelected));
                OnPropertyChanged(nameof(IsSettingsSelected));
            }
        }
    }

    public string CurrentTitle => CurrentSection switch
    {
        AppSection.LiveSites => "已上架网站",
        AppSection.Publish => "上架网站",
        AppSection.Transfers => "传输中心",
        AppSection.Trash => "回收站",
        AppSection.Settings => "设置",
        _ => string.Empty
    };

    public string CurrentDescription => CurrentSection switch
    {
        AppSection.LiveSites => "集中查看、分享和维护服务器上的展示网页。",
        AppSection.Publish => "选择一个包含 index.html 的文件夹并发布。",
        AppSection.Transfers => "查看扫描、压缩、上传和发布进度。",
        AppSection.Trash => "恢复站点，或管理将在 30 天后清理的内容。",
        AppSection.Settings => "配置服务器连接、SSH 私钥路径和安全指纹。",
        _ => string.Empty
    };

    public string ServerStatusTitle => _isServerConfigured ? "服务器已配置" : "服务器尚未配置";

    public string ServerStatusDescription => _isServerConfigured
        ? $"已连接配置：{_serverHost}"
        : "打开设置页完成 SSH 连接信息";

    public IRelayCommand<AppSection> NavigateCommand { get; }

    public LiveSitesViewModel LiveSites { get; }

    public PublishViewModel Publish { get; }

    public TransferCenterViewModel Transfers { get; }

    public TrashViewModel Trash { get; }

    public SettingsViewModel Settings { get; }

    public object? CurrentPage => CurrentSection switch
    {
        AppSection.LiveSites => LiveSites,
        AppSection.Publish => Publish,
        AppSection.Transfers => Transfers,
        AppSection.Trash => Trash,
        AppSection.Settings => Settings,
        _ => null
    };

    public bool IsLiveSitesSelected => IsSelected(AppSection.LiveSites);

    public bool IsPublishSelected => IsSelected(AppSection.Publish);

    public bool IsTransfersSelected => IsSelected(AppSection.Transfers);

    public bool IsTrashSelected => IsSelected(AppSection.Trash);

    public bool IsSettingsSelected => IsSelected(AppSection.Settings);

    public bool IsSelected(AppSection section) => CurrentSection == section;

    private void Navigate(AppSection section)
    {
        if (section == AppSection.Publish)
        {
            Publish.BeginNew();
        }

        CurrentSection = section;
    }

    private void BeginUpdate(SiteManager.Core.Models.SiteManifest site)
    {
        Publish.BeginUpdate(site);
        CurrentSection = AppSection.Publish;
    }

    private void ShowTransfers() => CurrentSection = AppSection.Transfers;
}
