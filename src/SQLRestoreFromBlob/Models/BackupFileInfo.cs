namespace SQLRestoreFromBlob.Models;

public enum BackupType
{
    Full,
    Differential,
    TransactionLog,
    Unknown
}

public class BackupFileInfo
{
    public string BlobName { get; set; } = string.Empty;
    public string BlobUrl { get; set; } = string.Empty;
    public BackupType Type { get; set; } = BackupType.Unknown;
    public long SizeBytes { get; set; }
    public DateTimeOffset LastModified { get; set; }

    // Populated from RESTORE HEADERONLY when connected to SQL Server
    public string? DatabaseName { get; set; }
    public DateTime? BackupStartDate { get; set; }
    public DateTime? BackupFinishDate { get; set; }
    public int? BackupTypeCode { get; set; }
    public decimal? FirstLsn { get; set; }
    public decimal? LastLsn { get; set; }
    public decimal? DatabaseBackupLsn { get; set; }

    public bool HasDetailedMetadata => BackupStartDate.HasValue;

    public DateTime EffectiveDate => BackupStartDate ?? LastModified.DateTime;

    public string TypeDisplay => Type switch
    {
        BackupType.Full => "Full",
        BackupType.Differential => "Differential",
        BackupType.TransactionLog => "Transaction Log",
        _ => "Unknown"
    };

    public string SizeDisplay
    {
        get
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double size = SizeBytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:F1} {units[unit]}";
        }
    }

    public override string ToString()
        => $"[{TypeDisplay}] {BlobName} ({SizeDisplay}) - {EffectiveDate:yyyy-MM-dd HH:mm:ss}";
}

public class BackupChain
{
    public BackupFileInfo Full { get; set; } = null!;
    public BackupFileInfo? Differential { get; set; }
    public List<BackupFileInfo> TransactionLogs { get; set; } = [];
    public DateTime? StopAt { get; set; }

    public IEnumerable<BackupFileInfo> AllFiles
    {
        get
        {
            yield return Full;
            if (Differential != null)
                yield return Differential;
            foreach (var log in TransactionLogs)
                yield return log;
        }
    }

    public long TotalSizeBytes => AllFiles.Sum(f => f.SizeBytes);

    public int FileCount => AllFiles.Count();

    public string Summary
    {
        get
        {
            var parts = new List<string> { "1 Full" };
            if (Differential != null) parts.Add("1 Diff");
            if (TransactionLogs.Count > 0) parts.Add($"{TransactionLogs.Count} Log(s)");
            return string.Join(" + ", parts);
        }
    }
}
