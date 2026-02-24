using System.Text;
using SQLRestoreFromBlob.Models;

namespace SQLRestoreFromBlob.Services;

public class RestoreScriptGenerator
{
    public string Generate(BackupChain chain, RestoreOptions options)
    {
        var sb = new StringBuilder();
        var dbName = EscapeName(options.TargetDatabaseName);
        var hasLogs = chain.TransactionLogs.Count > 0;
        var usePit = options.StopAt.HasValue && hasLogs;

        AppendHeader(sb, options, chain);

        AppendCredentialBlock(sb, options);

        if (options.DisconnectSessions)
            AppendDisconnectSessions(sb, dbName);

        AppendFullRestore(sb, chain.Full, options, chain.Differential != null || hasLogs);

        if (chain.Differential != null)
            AppendDiffRestore(sb, chain.Differential, options, hasLogs);

        for (int i = 0; i < chain.TransactionLogs.Count; i++)
        {
            bool isLast = i == chain.TransactionLogs.Count - 1;
            AppendLogRestore(sb, chain.TransactionLogs[i], options, isLast, usePit);
        }

        if (!hasLogs && chain.Differential == null && options.RecoveryMode == RecoveryMode.Recovery)
        {
            // Full-only restore already has RECOVERY
        }
        else if (!hasLogs && chain.Differential != null && options.RecoveryMode == RecoveryMode.Recovery)
        {
            // Diff restore already has RECOVERY
        }

        if (options.DisconnectSessions && options.RecoveryMode == RecoveryMode.Recovery)
            AppendReconnectSessions(sb, dbName);

        AppendFooter(sb);

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, RestoreOptions options, BackupChain chain)
    {
        sb.AppendLine("-- ============================================================");
        sb.AppendLine("-- SQL Restore From Blob - Generated Restore Script");
        sb.AppendLine($"-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"-- Target Database: {options.TargetDatabaseName}");
        sb.AppendLine($"-- Restore Chain: {chain.Summary}");
        if (options.StopAt.HasValue)
            sb.AppendLine($"-- Point-in-Time: {options.StopAt.Value:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("-- ============================================================");
        sb.AppendLine();
    }

    private static void AppendCredentialBlock(StringBuilder sb, RestoreOptions options)
    {
        if (string.IsNullOrEmpty(options.SasToken) || string.IsNullOrEmpty(options.StorageAccountUrl))
            return;

        var credName = EscapeName(options.SqlCredentialName);
        var cleanSas = options.SasToken.TrimStart('?');

        sb.AppendLine("-- Create/recreate the credential for blob access");
        sb.AppendLine($"IF EXISTS (SELECT 1 FROM sys.credentials WHERE name = '{options.SqlCredentialName}')");
        sb.AppendLine($"    DROP CREDENTIAL {credName};");
        sb.AppendLine();
        sb.AppendLine($"CREATE CREDENTIAL {credName}");
        sb.AppendLine("    WITH IDENTITY = 'SHARED ACCESS SIGNATURE',");
        sb.AppendLine($"    SECRET = '{cleanSas}';");
        sb.AppendLine("GO");
        sb.AppendLine();
    }

    private static void AppendDisconnectSessions(StringBuilder sb, string dbName)
    {
        sb.AppendLine("-- Disconnect all active sessions");
        sb.AppendLine($"IF DB_ID('{dbName.Replace("[", "").Replace("]", "")}') IS NOT NULL");
        sb.AppendLine("BEGIN");
        sb.AppendLine($"    ALTER DATABASE {dbName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;");
        sb.AppendLine("END");
        sb.AppendLine("GO");
        sb.AppendLine();
    }

    private static void AppendFullRestore(
        StringBuilder sb, BackupFileInfo full, RestoreOptions options, bool moreToFollow)
    {
        var dbName = EscapeName(options.TargetDatabaseName);
        var credName = EscapeName(options.SqlCredentialName);
        var recoveryClause = moreToFollow ? "NORECOVERY" : GetRecoveryClause(options);

        sb.AppendLine($"-- Restore FULL backup: {full.BlobName}");
        sb.AppendLine($"RESTORE DATABASE {dbName}");
        sb.AppendLine($"    FROM URL = '{full.BlobUrl}'");
        sb.AppendLine($"    WITH CREDENTIAL = '{options.SqlCredentialName}',");

        if (options.WithReplace)
            sb.AppendLine("         REPLACE,");

        AppendFileMoves(sb, options);

        sb.AppendLine($"         {recoveryClause},");
        AppendCommonOptions(sb, options);
        sb.AppendLine("GO");
        sb.AppendLine();
    }

    private static void AppendDiffRestore(
        StringBuilder sb, BackupFileInfo diff, RestoreOptions options, bool moreToFollow)
    {
        var dbName = EscapeName(options.TargetDatabaseName);
        var recoveryClause = moreToFollow ? "NORECOVERY" : GetRecoveryClause(options);

        sb.AppendLine($"-- Restore DIFFERENTIAL backup: {diff.BlobName}");
        sb.AppendLine($"RESTORE DATABASE {dbName}");
        sb.AppendLine($"    FROM URL = '{diff.BlobUrl}'");
        sb.AppendLine($"    WITH CREDENTIAL = '{options.SqlCredentialName}',");
        sb.AppendLine($"         {recoveryClause},");
        AppendCommonOptions(sb, options);
        sb.AppendLine("GO");
        sb.AppendLine();
    }

    private static void AppendLogRestore(
        StringBuilder sb, BackupFileInfo log, RestoreOptions options, bool isLast, bool usePit)
    {
        var dbName = EscapeName(options.TargetDatabaseName);
        var recoveryClause = isLast ? GetRecoveryClause(options) : "NORECOVERY";

        sb.AppendLine($"-- Restore LOG backup: {log.BlobName}");
        sb.AppendLine($"RESTORE LOG {dbName}");
        sb.AppendLine($"    FROM URL = '{log.BlobUrl}'");
        sb.AppendLine($"    WITH CREDENTIAL = '{options.SqlCredentialName}',");

        if (isLast && usePit && options.StopAt.HasValue)
            sb.AppendLine($"         STOPAT = '{options.StopAt.Value:yyyy-MM-ddTHH:mm:ss}',");

        sb.AppendLine($"         {recoveryClause},");
        AppendCommonOptions(sb, options);
        sb.AppendLine("GO");
        sb.AppendLine();
    }

    private static void AppendFileMoves(StringBuilder sb, RestoreOptions options)
    {
        foreach (var move in options.FileMoves)
        {
            if (move.NewPhysicalName != move.PhysicalName)
            {
                sb.AppendLine($"         MOVE '{move.LogicalName}' TO '{move.NewPhysicalName}',");
            }
        }
    }

    private static void AppendCommonOptions(StringBuilder sb, RestoreOptions options)
    {
        if (options.KeepReplication)
            sb.AppendLine("         KEEP_REPLICATION,");
        if (options.EnableBroker)
            sb.AppendLine("         ENABLE_BROKER,");
        if (options.NewBroker)
            sb.AppendLine("         NEW_BROKER,");
        sb.AppendLine($"         STATS = {options.StatsPercent};");
    }

    private static void AppendReconnectSessions(StringBuilder sb, string dbName)
    {
        sb.AppendLine("-- Return database to multi-user mode");
        sb.AppendLine($"ALTER DATABASE {dbName} SET MULTI_USER;");
        sb.AppendLine("GO");
        sb.AppendLine();
    }

    private static void AppendFooter(StringBuilder sb)
    {
        sb.AppendLine("-- ============================================================");
        sb.AppendLine("-- Restore script complete.");
        sb.AppendLine("-- ============================================================");
    }

    private static string GetRecoveryClause(RestoreOptions options)
    {
        return options.RecoveryMode switch
        {
            RecoveryMode.Recovery => "RECOVERY",
            RecoveryMode.NoRecovery => "NORECOVERY",
            RecoveryMode.Standby => $"STANDBY = '{options.StandbyFilePath}'",
            _ => "RECOVERY"
        };
    }

    private static string EscapeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "[DatabaseName]";
        if (name.StartsWith('[')) return name;
        return $"[{name}]";
    }
}
