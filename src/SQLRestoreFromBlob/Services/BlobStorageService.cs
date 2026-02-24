using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SQLRestoreFromBlob.Models;

namespace SQLRestoreFromBlob.Services;

public class BlobStorageService
{
    private readonly CredentialStore _credentialStore;

    public BlobStorageService(CredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
    }

    private BlobContainerClient CreateClient(BlobContainerConfig config)
    {
        var sasToken = _credentialStore.GetSasToken(config);
        if (string.IsNullOrEmpty(sasToken))
            throw new InvalidOperationException(
                "No SAS token found. Please configure the SAS token for this container.");

        var uriBuilder = new UriBuilder(config.ContainerUrl);
        uriBuilder.Query = sasToken.TrimStart('?');
        return new BlobContainerClient(uriBuilder.Uri);
    }

    public async Task<bool> VerifyConnectionAsync(BlobContainerConfig config, CancellationToken ct = default)
    {
        var client = CreateClient(config);
        var props = await client.GetPropertiesAsync(cancellationToken: ct);
        return props != null;
    }

    public async Task<ContainerSummary> GetContainerSummaryAsync(
        BlobContainerConfig config, CancellationToken ct = default)
    {
        var files = await ListBackupFilesAsync(config, ct);
        var summary = new ContainerSummary
        {
            TotalFiles = files.Count,
            FullBackups = files.Count(f => f.Type == BackupType.Full),
            DiffBackups = files.Count(f => f.Type == BackupType.Differential),
            LogBackups = files.Count(f => f.Type == BackupType.TransactionLog),
            UnknownFiles = files.Count(f => f.Type == BackupType.Unknown),
            TotalSizeBytes = files.Sum(f => f.SizeBytes),
            EarliestBackup = files.Count > 0 ? files.Min(f => f.LastModified) : null,
            LatestBackup = files.Count > 0 ? files.Max(f => f.LastModified) : null
        };
        return summary;
    }

    public async Task<List<BackupFileInfo>> ListBackupFilesAsync(
        BlobContainerConfig config, CancellationToken ct = default)
    {
        var client = CreateClient(config);
        var files = new List<BackupFileInfo>();

        await foreach (var blob in client.GetBlobsAsync(
            BlobTraits.Metadata, BlobStates.None, prefix: null, cancellationToken: ct))
        {
            var backupType = InferBackupType(blob.Name);

            var blobUrl = $"{config.ContainerUrl.TrimEnd('/')}/{blob.Name}";

            files.Add(new BackupFileInfo
            {
                BlobName = blob.Name,
                BlobUrl = blobUrl,
                Type = backupType,
                SizeBytes = blob.Properties.ContentLength ?? 0,
                LastModified = blob.Properties.LastModified ?? DateTimeOffset.MinValue
            });
        }

        return files.OrderBy(f => f.LastModified).ToList();
    }

    public string BuildBlobUrlWithSas(BlobContainerConfig config, string blobName)
    {
        var sasToken = _credentialStore.GetSasToken(config);
        return $"{config.ContainerUrl.TrimEnd('/')}/{blobName}?{sasToken?.TrimStart('?')}";
    }

    private static BackupType InferBackupType(string blobName)
    {
        var name = blobName.ToLowerInvariant();

        if (name.EndsWith(".trn") || name.EndsWith(".log"))
            return BackupType.TransactionLog;

        if (name.EndsWith(".bak") || name.EndsWith(".bkp"))
        {
            if (ContainsDiffIndicator(name))
                return BackupType.Differential;
            return BackupType.Full;
        }

        if (name.EndsWith(".diff"))
            return BackupType.Differential;

        return BackupType.Unknown;
    }

    private static bool ContainsDiffIndicator(string name)
    {
        string[] diffIndicators = ["diff", "differential", "_diff", "-diff", ".diff"];
        return diffIndicators.Any(name.Contains);
    }
}

public class ContainerSummary
{
    public int TotalFiles { get; set; }
    public int FullBackups { get; set; }
    public int DiffBackups { get; set; }
    public int LogBackups { get; set; }
    public int UnknownFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public DateTimeOffset? EarliestBackup { get; set; }
    public DateTimeOffset? LatestBackup { get; set; }

    public string TotalSizeDisplay
    {
        get
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double size = TotalSizeBytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:F1} {units[unit]}";
        }
    }
}
