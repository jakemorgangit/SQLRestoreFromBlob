using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLRestoreFromBlob.Models;
using SQLRestoreFromBlob.Services;

namespace SQLRestoreFromBlob.ViewModels;

public partial class BlobBrowserViewModel : ViewModelBase
{
    private readonly BlobStorageService _blobService;
    private readonly CredentialStore _credentialStore;

    [ObservableProperty]
    private ObservableCollection<BlobContainerConfig> _containers = [];

    [ObservableProperty]
    private BlobContainerConfig? _selectedContainer;

    [ObservableProperty]
    private ObservableCollection<BackupFileInfo> _backupFiles = [];

    [ObservableProperty]
    private ObservableCollection<BackupFileInfo> _filteredFiles = [];

    [ObservableProperty]
    private BackupFileInfo? _selectedFile;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _showFull = true;

    [ObservableProperty]
    private bool _showDiff = true;

    [ObservableProperty]
    private bool _showLog = true;

    [ObservableProperty]
    private bool _showUnknown = true;

    [ObservableProperty]
    private ContainerSummary? _summary;

    [ObservableProperty]
    private bool _hasFiles;

    public BlobBrowserViewModel(BlobStorageService blobService, CredentialStore credentialStore)
    {
        _blobService = blobService;
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

    partial void OnSelectedContainerChanged(BlobContainerConfig? value)
    {
        BackupFiles.Clear();
        FilteredFiles.Clear();
        Summary = null;
        HasFiles = false;
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnShowFullChanged(bool value) => ApplyFilter();
    partial void OnShowDiffChanged(bool value) => ApplyFilter();
    partial void OnShowLogChanged(bool value) => ApplyFilter();
    partial void OnShowUnknownChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private void Refresh()
    {
        LoadContainers();
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
            var files = await _blobService.ListBackupFilesAsync(SelectedContainer);
            BackupFiles = new ObservableCollection<BackupFileInfo>(files);
            HasFiles = files.Count > 0;

            Summary = await _blobService.GetContainerSummaryAsync(SelectedContainer);

            ApplyFilter();
            SetStatus($"Loaded {files.Count} backup files.");
        }
        catch (Exception ex)
        {
            SetError($"Failed to load backups: {ex.Message}");
            HasFiles = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var filtered = BackupFiles.AsEnumerable();

        if (!ShowFull) filtered = filtered.Where(f => f.Type != BackupType.Full);
        if (!ShowDiff) filtered = filtered.Where(f => f.Type != BackupType.Differential);
        if (!ShowLog) filtered = filtered.Where(f => f.Type != BackupType.TransactionLog);
        if (!ShowUnknown) filtered = filtered.Where(f => f.Type != BackupType.Unknown);

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            filtered = filtered.Where(f =>
                f.BlobName.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
        }

        FilteredFiles = new ObservableCollection<BackupFileInfo>(filtered);
    }
}
