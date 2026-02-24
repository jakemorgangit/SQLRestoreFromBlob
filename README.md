# SQL Restore From Blob

A production-ready Windows desktop utility for restoring SQL Server databases from Azure Blob Storage backups, with full support for point-in-time recovery using Full, Differential, and Transaction Log backup chains.

## Features

- **Azure Blob Storage Integration** -- Browse and verify backup files stored in Azure Blob containers using SAS token authentication
- **Point-in-Time Restore** -- Visual timeline picker to select a precise restore point across Full + Differential + Transaction Log backup chains
- **T-SQL Script Generation** -- Generates complete, ready-to-run restore scripts including credential creation, session management, and multi-step restore sequences
- **Direct Execution** -- Optionally execute restore scripts against a connected SQL Server instance with live progress reporting
- **Secure Credential Storage** -- SAS tokens and SQL Server passwords stored in Windows Credential Manager (encrypted at rest by the OS)
- **SAS Token Expiry Detection** -- Automatically parses SAS token expiry and warns when tokens are expired or approaching expiry
- **Comprehensive Restore Options** -- WITH REPLACE, RECOVERY/NORECOVERY/STANDBY, STOPAT, disconnect sessions, MOVE files, KEEP_REPLICATION, Service Broker options, and more
- **Dark Mode UI** -- Sleek, modern dark theme inspired by professional SQL Server tooling

## Requirements

- Windows 10/11 (x64)
- .NET 8.0 Runtime (included in self-contained builds)
- Azure Blob Storage container with SQL Server backup files
- Valid SAS token with read/list permissions on the container

## Quick Start

1. **Configure Blob Storage** -- Add your Azure Blob container URL and SAS token
2. **Add SQL Server** (optional) -- Configure a SQL Server connection for direct execution
3. **Browse Backups** -- Load and explore the backup files in your container
4. **Restore** -- Select a point-in-time, configure options, generate the script, and optionally execute it

## Building from Source

```bash
# Clone the repository
git clone https://github.com/your-repo/SQLRestoreFromBlob.git
cd SQLRestoreFromBlob

# Build
dotnet build src/SQLRestoreFromBlob/SQLRestoreFromBlob.csproj

# Run
dotnet run --project src/SQLRestoreFromBlob/SQLRestoreFromBlob.csproj

# Publish as single-file EXE
dotnet publish src/SQLRestoreFromBlob/SQLRestoreFromBlob.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/
```

## Restore Options Reference

| Option | Description |
|--------|-------------|
| **WITH REPLACE** | Overwrite existing database, even if created from a different backup set |
| **RECOVERY** | Database is fully recovered and ready for use (default) |
| **NORECOVERY** | Leaves database in restoring state for additional backup restores |
| **STANDBY** | Read-only mode with undo file; allows queries while accepting more restores |
| **Disconnect Sessions** | Forces all connections closed before restore (SINGLE_USER WITH ROLLBACK IMMEDIATE) |
| **STOPAT** | Point-in-time recovery target (auto-set from timeline picker) |
| **STATS** | Progress reporting interval (e.g., STATS = 10 reports every 10%) |
| **KEEP_REPLICATION** | Preserves replication settings during restore |
| **ENABLE_BROKER** | Enables Service Broker with existing contract after restore |
| **NEW_BROKER** | Creates new Service Broker ID, ending existing conversations |

## Architecture

```
src/SQLRestoreFromBlob/
  Models/          -- Data models (BackupFileInfo, RestoreOptions, ServerConnection, etc.)
  Services/        -- Business logic (CredentialStore, BlobStorage, SqlServer, ChainBuilder, ScriptGenerator)
  ViewModels/      -- MVVM ViewModels (CommunityToolkit.Mvvm)
  Views/           -- WPF XAML views
  Themes/          -- Dark theme resource dictionary
  Converters/      -- WPF value converters
```

## Security

- All secrets (SAS tokens, SQL passwords) are stored in **Windows Credential Manager**, encrypted at rest by the operating system
- Credentials are never written to disk in plain text
- The application runs entirely on your local machine; no telemetry or external calls beyond Azure Blob Storage access

## License

See [LICENSE](LICENSE) for details.
