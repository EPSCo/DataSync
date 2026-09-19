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
- **Real-time data**: copies new remote rows as they appear. It shows the last copied record and its time.
- **Historical data**: back-fills older records missing locally, newest first. *Pause/Resume sync task* stops
  and restarts it. The real-time task keeps running.
- **Sync table** tab: the record ranges and their state. *Count* is the records in the range, *Total (%)* its
  part of all rows shown. The *Sync* switch skips a range (off) or syncs it (on); it is dimmed for *Real-time*
  and *Synced* rows, which always show it checked. *Syncing* rows are yellow, the *Real-time* row is green,
  and switched-off (*Skipped*) rows show no progress until switched back on.
- **Messages** tab: recent events and errors, newest first. *Open log folder* opens the folder with the full log
  files (see [Logs and troubleshooting](#logs-and-troubleshooting)).
- **Test remote / local connection**: checks a database connection.
- **Clear local data**: asks for the daily password, stops replication, empties the local run-time tables and
  restarts.
- **Settings** (top-right of the header): edits the rig name, replication tuning and logging settings below. The
  values are checked, saved to `DataSync.exe.config` and applied after a restart, which DataSync offers to do.
  Saving needs write access to the application folder.
- **Batch** (on each task): how many rows the next remote read asks for, and the recent copy rate. With adaptive
  batch size on (the default), Real-Time reads carry a few seconds' worth of new records at the recent arrival rate
  (`RealTimeBatchMultiplier`, e.g. 2 records/s × 5 = 10 rows) and grow while more are waiting. Sync reads use steps
  (5, 10, 20, 30 … 100, 200 …): a size is kept while it copies well, a neighbouring step is tried now and then
  and kept only if it copies clearly more records per second over `SyncBatchMeasurements` measurements, tries that
  don't help are spaced further apart, and smaller steps are tried when reads stay much slower for as long. Both shrink after failures or timeouts. Turn it off in Settings to always read the row
  limits.
- **Latest data first** (`PrioritizeLatestData`, on by default): the newest data always comes first. After a network
  outage, or when the link is too slow to keep up, the Real-Time task jumps to the newest records (status
  *Catching up*) instead of copying the backlog oldest-first. The records it skipped are handed to the Sync task at
  once and back-filled newest first, so playback history fills in from the present backwards. While Real-Time is
  behind or failing, the Sync task waits (status *Waiting for real-time*) so it does not compete for the link.
- **Network outages**: both tasks keep retrying on their own. With latest data first on, the Real-Time task retries at
  least every 10 seconds, so new records flow again soon after the link returns.
- If a task fails too many times in a row (`TimeoutCounterLimit`), DataSync restarts itself, but only when both
  databases answer. While a database is unreachable a restart cannot help, so DataSync keeps running and retrying
  (the message list says so once).

## Configuration (`DataSync.exe.config`)

| Key | Meaning |
|---|---|
| `RigName` | Rig name shown in the header |
| `RealTimeUpdateInterval` | Seconds between real-time reads once caught up |
| `RealTimeRowLimit` / `SyncRowLimit` | Maximum rows per real-time read / per sync batch (every read when `AdaptiveBatchSize` is `false`) |
| `AdaptiveBatchSize` | `true` (default): size each read between `MinRowLimit` and the row limits: real-time from the rate new records arrive, sync for the most records per second |
| `MinRowLimit` | Smallest adaptive read size, and the size of the first read (default 5) |
| `RealTimeBatchMultiplier` | Polls' worth of new records one adaptive real-time read carries (default 5: 2 records/s × 5 = 10 rows) |
| `SyncBatchMeasurements` | Measurements (each at least 5 reads and 5 seconds) a new sync batch size is judged over (default 3); raise it if the sync batch size changes too often |
| `CommandTimeout` | Seconds before a database read or save is abandoned as timed out (minimum 5) |
| `PrioritizeLatestData` | `true` (default): newest records first, skipped records back-filled newest first; `false`: catch up oldest-first, as DDRREP did |
| `SyncUpdateInterval` | Seconds to pause between sync batches |
| `MaxRowsPerSecond` | Copy rate limit per task (0 = unlimited) |
| `GapCheckInterval` | Minutes between checks for new gaps |
| `TimeoutCounterLimit` | Consecutive failures before restart (skipped while a database is unreachable) |
| `LogPathDir`, `RollingInterval`, `RollOnFileSizeLimit`, `FileSizeLimitBytes`, `RetainedFileCountLimit` | Logging |
| `LogLevel` | `Information` (default), `Debug` (also every remote read, for troubleshooting a network), `Warning` or `Error` |

## Logs and troubleshooting

DataSync writes log files to the `logs` folder next to `DataSync.exe` (`LogPathDir`), one file per day by default,
named like `DataSync-20260915.log`. Click *Open log folder* on the **Messages** tab to find them, and send the files
covering the problem.

At the default level (`Information`) the log contains:

- **Startup**: version and build date, computer, Windows user, OS, time zone, IP addresses, the replication
  settings in effect, and the server, database and login name of each connection (never passwords). It also records
  each connection check, with how long it took, and hardware lock problems.
- **Every minute, per task**: reads, rows copied and rows per second, average and slowest read time, failures,
  batch size, and position. For Real-Time this is the newest local record and its age. For Sync it is the ranges
  and records left and the time spent waiting for Real-Time.
- **Events**: errors, with the number of consecutive failures and the next retry. A repeated identical error is
  logged on one line, without the stack trace. Also recoveries (with how long the task was failing), jumps to the
  newest records, catching up, sync ranges started and finished, batch-size reductions, long waits for Real-Time,
  pause/resume, and restart decisions.

To troubleshoot a slow or unstable network, set **Log level** to `Debug` in Settings and restart. Every real-time and
sync read is then logged with its record range, read time and save time. This writes several MB per day, so also
raise *File size limit* (e.g. `10485760`) and *Retained files* (e.g. `30`), and set it back to `Information`
afterwards.
