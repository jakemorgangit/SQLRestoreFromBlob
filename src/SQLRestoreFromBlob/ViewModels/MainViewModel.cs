using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLRestoreFromBlob.Services;

namespace SQLRestoreFromBlob.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly CredentialStore _credentialStore;
    private readonly BlobStorageService _blobService;
    private readonly SqlServerService _sqlService;
    private readonly BackupChainBuilder _chainBuilder;
    private readonly RestoreScriptGenerator _scriptGenerator;

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private string _currentViewName = "Blob Storage";

    [ObservableProperty]
    private string _globalStatus = "Ready";

    [ObservableProperty]
    private bool _isConnectedToSql;

    [ObservableProperty]
    private string _connectedServerName = "Not connected";

    public BlobConfigViewModel BlobConfig { get; }
    public ServerManagerViewModel ServerManager { get; }
    public BlobBrowserViewModel BlobBrowser { get; }
    public RestoreViewModel Restore { get; }

    public MainViewModel()
    {
        _credentialStore = new CredentialStore();
        _blobService = new BlobStorageService(_credentialStore);
        _sqlService = new SqlServerService(_credentialStore);
        _chainBuilder = new BackupChainBuilder();
        _scriptGenerator = new RestoreScriptGenerator();

        BlobConfig = new BlobConfigViewModel(_credentialStore, _blobService);
        ServerManager = new ServerManagerViewModel(_credentialStore, _sqlService);
        BlobBrowser = new BlobBrowserViewModel(_blobService, _credentialStore);
        Restore = new RestoreViewModel(_blobService, _sqlService, _chainBuilder, _scriptGenerator, _credentialStore);

        ServerManager.ConnectionChanged += OnSqlConnectionChanged;

        CurrentView = BlobConfig;
    }

    private void OnSqlConnectionChanged(object? sender, ServerConnectionChangedEventArgs e)
    {
        IsConnectedToSql = e.IsConnected;
        ConnectedServerName = e.IsConnected ? e.ServerName : "Not connected";
        Restore.IsConnectedToServer = e.IsConnected;
        Restore.ConnectedServerName = e.ServerName;
        GlobalStatus = e.IsConnected ? $"Connected to {e.ServerName}" : "Ready";
    }

    [RelayCommand]
    private void NavigateTo(string viewName)
    {
        CurrentViewName = viewName;
        CurrentView = viewName switch
        {
            "Blob Storage" => BlobConfig,
            "SQL Servers" => ServerManager,
            "Browse Backups" => BlobBrowser,
            "Restore" => Restore,
            _ => BlobConfig
        };
    }
}

public class ServerConnectionChangedEventArgs : EventArgs
{
    public bool IsConnected { get; init; }
    public string ServerName { get; init; } = string.Empty;
}
