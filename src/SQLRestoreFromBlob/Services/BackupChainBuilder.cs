using SQLRestoreFromBlob.Models;

namespace SQLRestoreFromBlob.Services;

public class BackupChainBuilder
{
    /// <summary>
    /// Builds the optimal restore chain for a given point-in-time target.
    /// Uses LSN metadata when available, otherwise falls back to timestamps.
    /// </summary>
    public BackupChain? BuildChain(List<BackupFileInfo> allBackups, DateTime targetTime)
    {
        var fulls = allBackups
            .Where(b => b.Type == BackupType.Full)
            .OrderByDescending(b => b.EffectiveDate)
            .ToList();

        var diffs = allBackups
            .Where(b => b.Type == BackupType.Differential)
            .OrderByDescending(b => b.EffectiveDate)
            .ToList();

        var logs = allBackups
            .Where(b => b.Type == BackupType.TransactionLog)
            .OrderBy(b => b.EffectiveDate)
            .ToList();

        var latestFull = fulls.FirstOrDefault(f => f.EffectiveDate <= targetTime);
        if (latestFull == null)
            return null;

        if (HasLsnMetadata(allBackups))
            return BuildChainWithLsn(latestFull, diffs, logs, targetTime);

        return BuildChainWithTimestamps(latestFull, diffs, logs, targetTime);
    }

    /// <summary>
    /// Returns the available restore window (earliest possible to latest possible).
    /// </summary>
    public (DateTime earliest, DateTime latest)? GetRestoreWindow(List<BackupFileInfo> allBackups)
    {
        var fulls = allBackups.Where(b => b.Type == BackupType.Full).ToList();
        if (fulls.Count == 0) return null;

        var earliest = fulls.Min(f => f.EffectiveDate);

        var allDates = allBackups.Select(b => b.EffectiveDate).ToList();
        var latest = allDates.Max();

        return (earliest, latest);
    }

    /// <summary>
    /// Returns all valid restore points (timestamps) from the available backups.
    /// </summary>
    public List<DateTime> GetRestorePoints(List<BackupFileInfo> allBackups)
    {
        var points = new HashSet<DateTime>();

        foreach (var backup in allBackups.OrderBy(b => b.EffectiveDate))
        {
            points.Add(backup.EffectiveDate);
            if (backup.BackupFinishDate.HasValue)
                points.Add(backup.BackupFinishDate.Value);
        }

        return points.OrderBy(p => p).ToList();
    }

    private static bool HasLsnMetadata(List<BackupFileInfo> backups)
        => backups.Any(b => b.FirstLsn.HasValue);

    private BackupChain BuildChainWithLsn(
        BackupFileInfo full, List<BackupFileInfo> diffs,
        List<BackupFileInfo> logs, DateTime targetTime)
    {
        var chain = new BackupChain { Full = full, StopAt = targetTime };

        var applicableDiff = diffs
            .Where(d => d.EffectiveDate <= targetTime
                        && d.DatabaseBackupLsn == full.LastLsn)
            .FirstOrDefault();

        if (applicableDiff != null)
        {
            chain.Differential = applicableDiff;
            var afterLsn = applicableDiff.LastLsn ?? 0m;
            chain.TransactionLogs = logs
                .Where(l => l.EffectiveDate <= targetTime
                            && l.FirstLsn >= afterLsn)
                .OrderBy(l => l.FirstLsn)
                .ToList();
        }
        else
        {
            var afterLsn = full.LastLsn ?? 0m;
            chain.TransactionLogs = logs
                .Where(l => l.EffectiveDate <= targetTime
                            && l.FirstLsn >= afterLsn)
                .OrderBy(l => l.FirstLsn)
                .ToList();
        }

        // Include the first log that covers the target time (its range spans past targetTime)
        var coveringLog = logs
            .Where(l => l.BackupStartDate <= targetTime
                        && l.BackupFinishDate >= targetTime)
            .FirstOrDefault();

        if (coveringLog != null && !chain.TransactionLogs.Contains(coveringLog))
            chain.TransactionLogs.Add(coveringLog);

        return chain;
    }

    private BackupChain BuildChainWithTimestamps(
        BackupFileInfo full, List<BackupFileInfo> diffs,
        List<BackupFileInfo> logs, DateTime targetTime)
    {
        var chain = new BackupChain { Full = full, StopAt = targetTime };

        var applicableDiff = diffs
            .Where(d => d.EffectiveDate > full.EffectiveDate
                        && d.EffectiveDate <= targetTime)
            .FirstOrDefault();

        var baseTime = full.EffectiveDate;

        if (applicableDiff != null)
        {
            chain.Differential = applicableDiff;
            baseTime = applicableDiff.EffectiveDate;
        }

        chain.TransactionLogs = logs
            .Where(l => l.EffectiveDate >= baseTime && l.EffectiveDate <= targetTime)
            .OrderBy(l => l.EffectiveDate)
            .ToList();

        // Include the log that covers the target time
        var nextLog = logs
            .Where(l => l.EffectiveDate > targetTime)
            .OrderBy(l => l.EffectiveDate)
            .FirstOrDefault();

        if (nextLog != null && chain.TransactionLogs.Count > 0)
        {
            // Only include if there's a gap to fill to reach targetTime
            var lastLogTime = chain.TransactionLogs.Last().EffectiveDate;
            if (lastLogTime < targetTime)
                chain.TransactionLogs.Add(nextLog);
        }
        else if (nextLog != null && chain.TransactionLogs.Count == 0)
        {
            chain.TransactionLogs.Add(nextLog);
        }

        return chain;
    }
}
