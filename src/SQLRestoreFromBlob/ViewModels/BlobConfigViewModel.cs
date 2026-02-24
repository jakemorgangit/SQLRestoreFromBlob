using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLRestoreFromBlob.Models;
using SQLRestoreFromBlob.Services;

namespace SQLRestoreFromBlob.ViewModels;

public partial class BlobConfigViewModel : ViewModelBase
{
    private readonly CredentialStore _credentialStore;
    private readonly BlobStorageService _blobService;

    [ObservableProperty]
    private ObservableCollection<BlobContainerConfig> _containers = [];

    [ObservableProperty]
    private BlobContainerConfig? _selectedContainer;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editContainerUrl = string.Empty;

    [ObservableProperty]
    private string _editSasToken = string.Empty;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isNew;

    [ObservableProperty]
    private string _testResult = string.Empty;

    [ObservableProperty]
    private bool _testSuccess;

    [ObservableProperty]
    private string? _sasExpiryText;

    [ObservableProperty]
    private bool _isSasExpired;

    [ObservableProperty]
    private ContainerSummary? _containerSummary;

    public BlobConfigViewModel(CredentialStore credentialStore, BlobStorageService blobService)
    {
        _credentialStore = credentialStore;
        _blobService = blobService;
        LoadContainers();
    }

    private void LoadContainers()
    {
        var config = _credentialStore.LoadConfig();
        Containers = new ObservableCollection<BlobContainerConfig>(config.BlobContainers);

        foreach (var container in Containers)
        {
            var sas = _credentialStore.GetSasToken(container);
            if (sas != null) container.CacheSasToken(sas);
        }
    }

    private void SaveContainers()
    {
        var config = _credentialStore.LoadConfig();
        config.BlobContainers = [.. Containers];
        _credentialStore.SaveConfig(config);
    }

    partial void OnSelectedContainerChanged(BlobContainerConfig? value)
    {
        if (value == null) return;
        UpdateSasExpiryStatus(value);
    }

    private void UpdateSasExpiryStatus(BlobContainerConfig container)
    {
        var expiry = _credentialStore.GetSasTokenExpiry(container);
        if (expiry.HasValue)
        {
            IsSasExpired = expiry.Value < DateTime.UtcNow;
            var remaining = expiry.Value - DateTime.UtcNow;
            SasExpiryText = IsSasExpired
                ? $"SAS token expired {-remaining.TotalHours:F0}h ago"
                : $"SAS token expires in {remaining.TotalHours:F0}h ({expiry.Value:yyyy-MM-dd HH:mm} UTC)";
        }
        else
        {
            SasExpiryText = "SAS token expiry unknown";
            IsSasExpired = false;
        }
    }

    [RelayCommand]
    private void AddNew()
    {
        EditName = string.Empty;
        EditContainerUrl = string.Empty;
        EditSasToken = string.Empty;
        IsNew = true;
        IsEditing = true;
        TestResult = string.Empty;
        ContainerSummary = null;
    }

    [RelayCommand]
    private void Edit()
    {
        if (SelectedContainer == null) return;
        EditName = SelectedContainer.Name;
        EditContainerUrl = SelectedContainer.ContainerUrl;
        EditSasToken = _credentialStore.GetSasToken(SelectedContainer) ?? string.Empty;
        IsNew = false;
        IsEditing = true;
        TestResult = string.Empty;
        ContainerSummary = null;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        ClearStatus();
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(EditName) || string.IsNullOrWhiteSpace(EditContainerUrl))
        {
            SetError("Name and Container URL are required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(EditSasToken))
        {
            SetError("SAS Token is required.");
            return;
        }

        BlobContainerConfig container;
        if (IsNew)
        {
            if (Containers.Any(c => c.Name.Equals(EditName, StringComparison.OrdinalIgnoreCase)))
            {
                SetError("A container with this name already exists.");
                return;
            }
            container = new BlobContainerConfig
            {
                Name = EditName,
                ContainerUrl = EditContainerUrl.TrimEnd('/')
            };
            Containers.Add(container);
        }
        else
        {
            container = SelectedContainer!;
            container.Name = EditName;
            container.ContainerUrl = EditContainerUrl.TrimEnd('/');
        }

        _credentialStore.SaveSasToken(container, EditSasToken);
        SaveContainers();

        SelectedContainer = container;
        IsEditing = false;
        UpdateSasExpiryStatus(container);
        SetStatus("Container saved successfully.");
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedContainer == null) return;
        _credentialStore.DeleteSecret(SelectedContainer.CredentialKey);
        Containers.Remove(SelectedContainer);
        SaveContainers();
        SelectedContainer = Containers.FirstOrDefault();
        SetStatus("Container removed.");
    }

    [RelayCommand]
    private void RefreshToken()
    {
        if (SelectedContainer == null) return;
        EditName = SelectedContainer.Name;
        EditContainerUrl = SelectedContainer.ContainerUrl;
        EditSasToken = string.Empty;
        IsNew = false;
        IsEditing = true;
        SetStatus("Please enter the new SAS token.");
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        var config = IsEditing
            ? new BlobContainerConfig { Name = EditName, ContainerUrl = EditContainerUrl }
            : SelectedContainer;

        if (config == null) return;

        if (IsEditing && !string.IsNullOrWhiteSpace(EditSasToken))
        {
            _credentialStore.SaveSasToken(config, EditSasToken);
        }

        IsBusy = true;
        TestResult = string.Empty;
        try
        {
            await _blobService.VerifyConnectionAsync(config);
            var summary = await _blobService.GetContainerSummaryAsync(config);
            ContainerSummary = summary;
            TestSuccess = true;
            TestResult = $"Connected! {summary.TotalFiles} files found ({summary.TotalSizeDisplay})";
        }
        catch (Exception ex)
        {
            TestSuccess = false;
            TestResult = $"Connection failed: {ex.Message}";
            ContainerSummary = null;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
