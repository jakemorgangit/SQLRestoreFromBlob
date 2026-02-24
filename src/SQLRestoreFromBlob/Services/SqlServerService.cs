using System.Data;
using Microsoft.Data.SqlClient;
using SQLRestoreFromBlob.Models;

namespace SQLRestoreFromBlob.Services;

public class SqlServerService
{
    private readonly CredentialStore _credentialStore;

    public SqlServerService(CredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
    }

    public string BuildConnectionString(ServerConnection server)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server.ServerName,
            ConnectTimeout = server.ConnectionTimeoutSeconds,
            TrustServerCertificate = server.TrustServerCertificate,
            Encrypt = server.Encrypt switch
            {
                EncryptMode.Yes => SqlConnectionEncryptOption.Mandatory,
                EncryptMode.Strict => SqlConnectionEncryptOption.Strict,
                _ => SqlConnectionEncryptOption.Optional
            },
            ApplicationName = "SQL Restore From Blob",
            MultipleActiveResultSets = false
        };

        if (server.AuthMode == AuthMode.WindowsAuth)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.IntegratedSecurity = false;
            builder.UserID = server.Username;
            builder.Password = _credentialStore.GetSqlPassword(server);
        }

        return builder.ConnectionString;
    }

    public async Task<bool> TestConnectionAsync(ServerConnection server, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(BuildConnectionString(server));
        await conn.OpenAsync(ct);
        return conn.State == ConnectionState.Open;
    }

    public async Task<string> GetServerVersionAsync(ServerConnection server, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(BuildConnectionString(server));
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT @@VERSION";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result?.ToString() ?? "Unknown";
    }

    public async Task<List<string>> GetDatabaseListAsync(ServerConnection server, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(BuildConnectionString(server));
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sys.databases ORDER BY name";
        var databases = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            databases.Add(reader.GetString(0));
        return databases;
    }

    public async Task<List<BackupFileInfo>> RestoreHeaderOnlyAsync(
        ServerConnection server, string blobUrl, string credentialName, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(BuildConnectionString(server));
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120;
        cmd.CommandText = $"RESTORE HEADERONLY FROM URL = N'{blobUrl.Replace("'", "''")}' WITH CREDENTIAL = N'{credentialName.Replace("'", "''")}'";

        var results = new List<BackupFileInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var backupTypeCode = reader.GetInt16(reader.GetOrdinal("BackupType"));
            results.Add(new BackupFileInfo
            {
                DatabaseName = reader.GetString(reader.GetOrdinal("DatabaseName")),
                BackupStartDate = reader.GetDateTime(reader.GetOrdinal("BackupStartDate")),
                BackupFinishDate = reader.GetDateTime(reader.GetOrdinal("BackupFinishDate")),
                BackupTypeCode = backupTypeCode,
                Type = backupTypeCode switch
                {
                    1 => BackupType.Full,
                    5 => BackupType.Differential,
                    2 => BackupType.TransactionLog,
                    _ => BackupType.Unknown
                },
                FirstLsn = reader.GetDecimal(reader.GetOrdinal("FirstLSN")),
                LastLsn = reader.GetDecimal(reader.GetOrdinal("LastLSN")),
                DatabaseBackupLsn = reader.GetDecimal(reader.GetOrdinal("DatabaseBackupLSN"))
            });
        }
        return results;
    }

    public async Task<List<FileMoveOption>> RestoreFileListOnlyAsync(
        ServerConnection server, string blobUrl, string credentialName, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(BuildConnectionString(server));
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120;
        cmd.CommandText = $"RESTORE FILELISTONLY FROM URL = N'{blobUrl.Replace("'", "''")}' WITH CREDENTIAL = N'{credentialName.Replace("'", "''")}'";

        var files = new List<FileMoveOption>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            files.Add(new FileMoveOption
            {
                LogicalName = reader.GetString(reader.GetOrdinal("LogicalName")),
                PhysicalName = reader.GetString(reader.GetOrdinal("PhysicalName")),
                Type = reader.GetString(reader.GetOrdinal("Type")),
                NewPhysicalName = reader.GetString(reader.GetOrdinal("PhysicalName"))
            });
        }
        return files;
    }

    public async Task ExecuteNonQueryAsync(
        ServerConnection server, string sql,
        Action<string>? messageCallback = null, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(BuildConnectionString(server));
        if (messageCallback != null)
        {
            conn.InfoMessage += (_, e) => messageCallback(e.Message);
        }
        conn.FireInfoMessageEventOnUserErrors = true;
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 0;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ExecuteRestoreWithProgressAsync(
        ServerConnection server, string sql,
        Action<string>? messageCallback = null, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(BuildConnectionString(server));
        if (messageCallback != null)
        {
            conn.InfoMessage += (_, e) => messageCallback(e.Message);
        }
        conn.FireInfoMessageEventOnUserErrors = true;
        await conn.OpenAsync(ct);

        var statements = SplitGoStatements(sql);
        foreach (var statement in statements)
        {
            if (string.IsNullOrWhiteSpace(statement)) continue;
            ct.ThrowIfCancellationRequested();

            messageCallback?.Invoke($"Executing: {statement[..Math.Min(80, statement.Length)].Trim()}...");

            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 0;
            cmd.CommandText = statement;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task EnsureCredentialExistsAsync(
        ServerConnection server, string credentialName, string storageAccountUrl, string sasToken,
        CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(BuildConnectionString(server));
        await conn.OpenAsync(ct);

        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sys.credentials WHERE name = @name";
        checkCmd.Parameters.AddWithValue("@name", credentialName);
        var exists = (int)(await checkCmd.ExecuteScalarAsync(ct))! > 0;

        if (exists)
        {
            await using var dropCmd = conn.CreateCommand();
            dropCmd.CommandText = $"DROP CREDENTIAL [{credentialName}]";
            await dropCmd.ExecuteNonQueryAsync(ct);
        }

        var cleanSas = sasToken.TrimStart('?');
        await using var createCmd = conn.CreateCommand();
        createCmd.CommandText = $@"
            CREATE CREDENTIAL [{credentialName}]
            WITH IDENTITY = 'SHARED ACCESS SIGNATURE',
            SECRET = '{cleanSas.Replace("'", "''")}'";
        await createCmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<(string DataPath, string LogPath)> GetDefaultPathsAsync(
        ServerConnection server, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(BuildConnectionString(server));
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT
            SERVERPROPERTY('InstanceDefaultDataPath') AS DefaultDataPath,
            SERVERPROPERTY('InstanceDefaultLogPath') AS DefaultLogPath";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var dataPath = reader["DefaultDataPath"]?.ToString() ?? string.Empty;
            var logPath = reader["DefaultLogPath"]?.ToString() ?? string.Empty;
            return (dataPath.TrimEnd('\\'), logPath.TrimEnd('\\'));
        }
        return (string.Empty, string.Empty);
    }

    private static List<string> SplitGoStatements(string sql)
    {
        var statements = new List<string>();
        var lines = sql.Split('\n');
        var current = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Length > 0)
                {
                    statements.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.AppendLine(line);
            }
        }

        if (current.Length > 0)
            statements.Add(current.ToString());

        return statements;
    }
}
