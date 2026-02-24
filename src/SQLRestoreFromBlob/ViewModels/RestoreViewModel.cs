using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SQLRestoreFromBlob.Models;
using SQLRestoreFromBlob.Services;

namespace SQLRestoreFromBlob.ViewModels;

public partial class RestoreViewModel : ViewModelBase
{
    private readonly BlobStorageService _blobService;
    private readonly SqlServerService _sqlService;
    private readonly BackupChainBuilder _chainBuilder;
    private readonly RestoreScriptGenerator _scriptGenerator;
    private readonly CredentialStore _credentialStore;

    private List<BackupFileInfo> _allBackups = [];

    #region Observable Properties

    [ObservableProperty]
    private ObservableCollection<BlobContainerConfig> _containers = [];

    [ObservableProperty]
    private BlobContainerConfig? _selectedContainer;

    [ObservableProperty]
    private bool _backupsLoaded;

    [ObservableProperty]
    private string _targetDatabaseName = string.Empty;

    // Timeline
    [ObservableProperty]
    private DateTime _timelineMin = DateTime.Now.AddDays(-7);

    [ObservableProperty]
    private DateTime _timelineMax = DateTime.Now;

    [ObservableProperty]
    private double _timelineSliderMin;

    [ObservableProperty]
    private double _timelineSliderMax = 100;

    [ObservableProperty]
    private double _timelineSliderValue = 100;

    [ObservableProperty]
    private DateTime _selectedDateTime = DateTime.Now;

    [ObservableProperty]
    private string _selectedDateTimeDisplay = string.Empty;

    // Restore chain
    [ObservableProperty]
    private BackupChain? _restoreChain;

    [ObservableProperty]
    private ObservableCollection<BackupFileInfo> _chainFiles = [];

    [ObservableProperty]
    private string _chainSummary = string.Empty;

    [ObservableProperty]
    private bool _hasValidChain;

    // Options
    [ObservableProperty]
    private bool _withReplace = true;

    [ObservableProperty]
    private RecoveryMode _recoveryMode = RecoveryMode.Recovery;

    public bool IsStandbyMode => RecoveryMode == RecoveryMode.Standby;

    partial void OnRecoveryModeChanged(RecoveryMode value) => OnPropertyChanged(nameof(IsStandbyMode));

    [ObservableProperty]
    private string _standbyFilePath = string.Empty;

    [ObservableProperty]
    private bool _disconnectSessions = true;

    [ObservableProperty]
    private int _statsPercent = 10;

    [ObservableProperty]
    private bool _keepReplication;

    [ObservableProperty]
    private bool _enableBroker;

    [ObservableProperty]
    private bool _newBroker;

    [ObservableProperty]
    private string _sqlCredentialName = "BlobRestoreCredential";

    [ObservableProperty]
    private ObservableCollection<FileMoveOption> _fileMoves = [];

    [ObservableProperty]
    private bool _hasFileMoves;

    // Script output
    [ObservableProperty]
    private string _generatedScript = string.Empty;

    [ObservableProperty]
    private bool _hasScript;

    // Execution
    [ObservableProperty]
    private bool _isConnectedToServer;

    [ObservableProperty]
    private string _connectedServerName = string.Empty;

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private string _executionLog = string.Empty;

    [ObservableProperty]
    private bool _executionComplete;

    [ObservableProperty]
    private bool _executionSuccess;

    // Backup summary
    [ObservableProperty]
    private int _fullCount;

    [ObservableProperty]
    private int _diffCount;

    [ObservableProperty]
    private int _logCount;

    #endregion

    public RestoreViewModel(
        BlobStorageService blobService,
        SqlServerService sqlService,
        BackupChainBuilder chainBuilder,
        RestoreScriptGenerator scriptGenerator,
        CredentialStore credentialStore)
    {
        _blobService = blobService;
        _sqlService = sqlService;
        _chainBuilder = chainBuilder;
        _scriptGenerator = scriptGenerator;
        _credentialStore = credentialStore;
        LoadContainers();
    }

    private void LoadContainers()
    {
        var config = _credentialStore.LoadConfig();
        Containers = new ObservableCollection<BlobContainerConfig>(config.BlobContainers);
        foreach (var c in Containers)
        {
            var sas = _credentialStore.GetSasToken(c);
            if (sas != null) c.CacheSasToken(sas);
        }
    }

    partial void OnTimelineSliderValueChanged(double value)
    {
        if (!BackupsLoaded) return;
        var range = (TimelineMax - TimelineMin).TotalSeconds;
        var seconds = (value / 100.0) * range;
        SelectedDateTime = TimelineMin.AddSeconds(seconds);
        SelectedDateTimeDisplay = SelectedDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        RebuildChain();
    }

    [RelayCommand]
    private async Task LoadBackupsAsync()
    {
        if (SelectedContainer == null)
        {
            SetError("Please select a container first.");
            return;
        }

        IsBusy = true;
        ClearStatus();
        try
        {
            _allBackups = await _blobService.ListBackupFilesAsync(SelectedContainer);

            FullCount = _allBackups.Count(b => b.Type == BackupType.Full);
            DiffCount = _allBackups.Count(b => b.Type == BackupType.Differential);
            LogCount = _allBackups.Count(b => b.Type == BackupType.TransactionLog);

            var window = _chainBuilder.GetRestoreWindow(_allBackups);
            if (window == null)
            {
                SetError("No full backups found in container. Cannot build a restore chain.");
                BackupsLoaded = false;
                return;
            }

            TimelineMin = window.Value.earliest;
            TimelineMax = window.Value.latest;
            TimelineSliderValue = 100;
            SelectedDateTime = TimelineMax;
            SelectedDateTimeDisplay = SelectedDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            BackupsLoaded = true;

            if (_allBackups.Count > 0 && string.IsNullOrEmpty(TargetDatabaseName))
            {
                var dbName = _allBackups.FirstOrDefault(b => !string.IsNullOrEmpty(b.DatabaseName))?.DatabaseName;
                if (!string.IsNullOrEmpty(dbName))
                    TargetDatabaseName = dbName;
            }

            RebuildChain();
            SetStatus($"Loaded {_allBackups.Count} backups. Restore window: {TimelineMin:yyyy-MM-dd HH:mm} to {TimelineMax:yyyy-MM-dd HH:mm}");
        }
        catch (Exception ex)
        {
            SetError($"Failed to load backups: {ex.Message}");
            BackupsLoaded = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildChain()
    {
        var chain = _chainBuilder.BuildChain(_allBackups, SelectedDateTime);
        RestoreChain = chain;
        HasValidChain = chain != null;

        if (chain != null)
        {
            ChainFiles = new ObservableCollection<BackupFileInfo>(chain.AllFiles);
            ChainSummary = $"{chain.Summary} | {chain.FileCount} files | Target: {SelectedDateTime:yyyy-MM-dd HH:mm:ss}";
        }
        else
        {
            ChainFiles.Clear();
            ChainSummary = "No valid restore chain available for selected time.";
        }
    }

    [RelayCommand]
    private void GenerateScript()
    {
        if (RestoreChain == null)
        {
            SetError("No valid restore chain. Load backups and select a restore point first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetDatabaseName))
        {
            SetError("Please enter a target database name.");
            return;
        }

        var sasToken = SelectedContainer != null
            ? _credentialStore.GetSasToken(SelectedContainer)
            : null;

        var options = new RestoreOptions
        {
            TargetDatabaseName = TargetDatabaseName,
            WithReplace = WithReplace,
            RecoveryMode = RecoveryMode,
            StandbyFilePath = string.IsNullOrWhiteSpace(StandbyFilePath) ? null : StandbyFilePath,
            DisconnectSessions = DisconnectSessions,
            StatsPercent = StatsPercent,
            StopAt = RestoreChain.TransactionLogs.Count > 0 ? SelectedDateTime : null,
            KeepReplication = KeepReplication,
            EnableBroker = EnableBroker,
            NewBroker = NewBroker,
            SqlCredentialName = SqlCredentialName,
            SasToken = sasToken,
            StorageAccountUrl = SelectedContainer?.ContainerUrl,
            FileMoves = [.. FileMoves]
        };

        GeneratedScript = _scriptGenerator.Generate(RestoreChain, options);
        HasScript = true;
        SetStatus("Script generated successfully.");
    }

    [RelayCommand]
    private void CopyScript()
    {
        if (!string.IsNullOrEmpty(GeneratedScript))
        {
            Clipboard.SetText(GeneratedScript);
            SetStatus("Script copied to clipboard.");
        }
    }

    [RelayCommand]
    private void SaveScript()
    {
        if (string.IsNullOrEmpty(GeneratedScript)) return;

        var dialog = new SaveFileDialog
        {
            Filter = "SQL Files (*.sql)|*.sql|All Files (*.*)|*.*",
            DefaultExt = ".sql",
            FileName = $"restore_{TargetDatabaseName}_{DateTime.Now:yyyyMMdd_HHmmss}.sql"
        };

        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, GeneratedScript);
            SetStatus($"Script saved to {dialog.FileName}");
        }
    }

    [RelayCommand]
    private async Task ExecuteScriptAsync()
    {
        if (string.IsNullOrEmpty(GeneratedScript) || !IsConnectedToServer)
            return;

        var result = MessageBox.Show(
            $"Execute restore script against {ConnectedServerName}?\n\n" +
            $"Database: {TargetDatabaseName}\n" +
            $"Chain: {ChainSummary}\n\n" +
            "This operation cannot be undone.",
            "Confirm Restore Execution",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsExecuting = true;
        ExecutionComplete = false;
        ExecutionLog = string.Empty;

        try
        {
            var config = _credentialStore.LoadConfig();
            var server = config.Servers.FirstOrDefault(s => s.ServerName == ConnectedServerName);
            if (server == null)
            {
                SetError("Connected server not found in config.");
                return;
            }

            // Ensure credential exists on the server
            if (SelectedContainer != null)
            {
                var sasToken = _credentialStore.GetSasToken(SelectedContainer);
                if (!string.IsNullOrEmpty(sasToken))
                {
                    AppendLog("Creating SQL Server credential for blob access...");
                    await _sqlService.EnsureCredentialExistsAsync(
                        server, SqlCredentialName, SelectedContainer.ContainerUrl, sasToken);
                    AppendLog("Credential created successfully.");
                }
            }

            AppendLog("Beginning restore execution...\n");

            await _sqlService.ExecuteRestoreWithProgressAsync(
                server,
                GeneratedScript,
                msg => Application.Current.Dispatcher.Invoke(() => AppendLog(msg)));

            ExecutionSuccess = true;
            AppendLog("\nRestore completed successfully!");
            SetStatus("Restore execution completed successfully.");
        }
        catch (Exception ex)
        {
            ExecutionSuccess = false;
            AppendLog($"\nERROR: {ex.Message}");
            SetError($"Restore failed: {ex.Message}");
        }
        finally
        {
            IsExecuting = false;
            ExecutionComplete = true;
        }
    }

    [RelayCommand]
    private void RefreshContainers()
    {
        LoadContainers();
    }

    private void AppendLog(string message)
    {
        ExecutionLog += message + "\n";
    }
}
