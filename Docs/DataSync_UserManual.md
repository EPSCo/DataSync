# DataSync – User Manual

**DataSync** copies drilling data from a remote (source) DDR/DDM database into a local (destination) database and
keeps the copy up to date. It replaces DDRREP and uses the same database setup and connection files.

## Setting up the local database (based on the v1.0 backup)

The local database is assumed to be named `OfficeDDR`. Run the scripts in `SQL\` in the order below, using
`sqlcmd` or SQL Server Management Studio (SSMS).

### Step 1: Restore the database from backup

Before running `Restore_OfficeDDR_Database_From_Backup.sql`, edit these parameters in the script:

- `@BackupFile`: path to the backup file, e.g. `'D:\BaseBackup\backup_202203061543\backup_202203061543.bak'`
- `@BackupLogicalDataFileName` / `@BackupLogicalLogFileName`: logical file names, e.g. `'DDR'` and `'DDR_log'`
- `@OfficeLogicalDataFilePath` / `@OfficeLogicalLogFilePath`: where the `OfficeDDR` data and log files are stored

> ⚠️ The target paths must exist, or the script fails.

```bash
sqlcmd -S localhost\MSSQLSERVERINS -E -i Restore_OfficeDDR_Database_From_Backup.sql
```

On a remote server, connect with `-S IP\INSTANCE -U sa -P YourPassword` or `-S IP,Port -U sa -P YourPassword` instead.

### Step 2: Truncate tables

```bash
sqlcmd -S localhost\MSSQLSERVERINS -E -i Truncate_Database.sql
```

### Step 3: Update the database schema to version 2

```bash
sqlcmd -S localhost\MSSQLSERVERINS -E -i SchemaUpdate_v2.sql
```

### Step 4: Replicator schema update

Removes the tables and stored procedures used only by DLogger, and adds the replicator's procedures.

```bash
sqlcmd -S localhost\MSSQLSERVERINS -E -i OfficeDDRSchemaUpdate_REP.sql
```

### Step 5: Prevent duplicate records (local database only)

Removes duplicate `BaseID` rows, adds a unique index on `TProcessData.BaseID`, and makes `TProcessData_Ins` skip
existing records.

```bash
sqlcmd -S localhost\MSSQLSERVERINS -E -d OfficeDDR -i Rep_TProcessData_NoDuplicates.sql
```

> ⚠️ Do not run this script on the remote (source) DDR/DDM database.

> 💡 `sqlcmd` can be downloaded from [GitHub Releases](https://github.com/microsoft/go-sqlcmd/releases).

## Database connections

DataSync needs both databases. The connection strings come only from two encrypted files next to `DataSync.exe`,
created with **DDUtility** for the machine DataSync runs on:

- `remote.eps`: the original DDR/DDM database
- `local.eps`: the replicated database

The files are bound to the machine's CPU. If they were created for another machine, DataSync reports
*Hardware lock verification failed* and exits.

## Using DataSync

- **Login**: sign in with a user from the local database. With *Remember me* checked, the next start logs in
  automatically.
- **Real-Time Data Task**: copies new remote rows as they appear. It shows the last copied record and its time.
- **Sync Old Data Task**: back-fills older records missing locally, newest first. *Pause/Resume Sync Task* stops
  and restarts it. The real-time task keeps running.
- **Details** tab: the record ranges and their state. *Syncing* rows are yellow, the *RealTime* row is green.
- **Messages** tab: recent events and errors, newest first. Full logs are in the `logs` folder.
- **Test Remote / Local Connection**: checks a database connection.
- **Clear Local Data**: asks for the daily password, stops replication, empties the local run-time tables and
  restarts.
- **Settings** (top-right of the header): edits the rig name, replication tuning and logging settings below. The
  values are checked, saved to `DataSync.exe.config` and applied after a restart, which DataSync offers to do.
  Saving needs write access to the application folder.
- If a task fails too many times in a row (`TimeoutCounterLimit`), DataSync restarts itself.

## Configuration (`DataSync.exe.config`)

| Key | Meaning |
|---|---|
| `RigName` | Rig name shown in the header |
| `RealTimeUpdateInterval` | Seconds between real-time reads once caught up |
| `RealTimeRowLimit` / `SyncRowLimit` | Rows per real-time read / per sync batch |
| `SyncUpdateInterval` | Seconds to pause between sync batches |
| `MaxRowsPerSecond` | Copy rate limit per task (0 = unlimited) |
| `GapCheckInterval` | Minutes between checks for new gaps |
| `TimeoutCounterLimit` | Consecutive failures before restart |
| `LogPathDir`, `RollingInterval`, `RollOnFileSizeLimit`, `FileSizeLimitBytes`, `RetainedFileCountLimit` | Logging |
