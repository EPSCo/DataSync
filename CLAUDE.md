# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> The `CLAUDE.md` in the parent folder (`C:\. Projects\`) describes **Daftar**, a different product. Ignore it here.

## What this is

DataSync is a .NET Framework 4.8 WPF rewrite of DDRREP (`C:\. Projects\DDRREP`). It replicates `TProcessData` rows
from a remote DDR/DDM SQL Server database into a local one. It must stay compatible with DDRREP's databases, `Rep_*`
stored procedures, `.eps` connection files, `PW.txt` and App.config keys.

## Hard rule: no component dependencies

`DataSync` and `DataSync.Core` must not reference NuGet packages or third-party DLLs; use only .NET Framework
assemblies. No Telerik, Serilog, Dapper, MVVM toolkits, etc. Only `DataSync.Tests` may use NuGet (MSTest).

## Build and test

- SDK-style projects targeting `net48`, C# 7.3 (set in `Directory.Build.props`). Files are picked up automatically.
- `dotnet build DataSync.sln` / `dotnet test DataSync.Tests\DataSync.Tests.csproj` (no database needed).
- UI work without databases: run a Debug build with `--design` (sample data, no checks, no login, no replication).

## Layout

- `DataSync.Core`
  - `Replication\`: the engine, ported unchanged in behaviour from DDRREP. `RealTimeReplicator` reads forward from
    `NextBaseId`. `GapSyncReplicator` back-fills below it, newest gap first, reading backwards. `SyncRangePlanner`
    holds the pure range arithmetic. `ReplicationTask` provides the background loop, 1–60 s back-off (real-time 1–10 s with `PrioritizeLatestData`) and
    throttling. `MainViewModel` restarts the app on `FailureLimitExceeded` only when both databases answer.
    `AdaptiveBatchSize` (DataSync-only, on by default) sizes each remote read between `MinRowLimit` and the row limit;
    with `AdaptiveBatchSize=false` reads are fixed at the row limit as in DDRREP. `RealTimeBatchSize` carries
    `RealTimeBatchMultiplier` polls' worth of new records at the average arrival rate (newest remote BaseID sampled
    over the last 10 polls) and doubles while reads come back full. `SyncBatchSize` holds a step (5, 10, 20 … 100, 200 …)
    and after 10 measurements (≥5 reads and ≥5 s each) tries a neighbouring step, keeping it only when BaseIDs copied
    per second rise by 10% over `SyncBatchMeasurements` measurements; each try that doesn't help doubles the wait (up to
    80); a 20% drop lasting `SyncBatchMeasurements` measurements tries smaller steps. Both shrink after failed reads (base `AdaptiveBatchSize`).
    `PrioritizeLatestData` (DataSync-only, on by default) puts the newest data first: when more rows are waiting than
    one read carries, `RealTimeReplicator` skips to the newest rows and raises `RangeSkipped`; the engine passes the
    range to `GapSyncReplicator.AddGap`, which wakes the sync task and copies it before older ranges, newest first.
    The sync task yields while real-time `IsBehind` or is failing. The skipped range is handed over after
    `NextBaseId` moves, so a refresh can only see it twice (ignored), never miss it.
  - `Data\`: `DatabaseConfig` (reads `.eps` files, hardware lock, connection checks), `SqlDb` (small ADO.NET
    stored-procedure mapper), `SqlProcessDataStore` (`IProcessDataStore` over SQL Server; `SaveBatch` uses
    `SqlBulkCopy` into a temp table plus `INSERT … WHERE NOT EXISTS`), `UserRepository`.
  - `Security\`: DES `Encryption` (must stay byte-compatible with DDUtility), `CredentialStore` (`PW.txt`),
    `ClearDataPassword` (date-based).
  - `Logging\Log`: static rolling file logger configured from App.config (`LogLevel` filters); never throws. The log is
    how field problems on other computers and networks are diagnosed: `ReplicationTask` logs a one-minute summary
    per task (plus `DescribeState`), failures with retry details (stack trace only when the error changes) and
    recoveries; every read is logged at Debug. Never log passwords or full connection strings
    (`DatabaseConfig.Describe`).
  - `Configuration\`: `SettingKeys` (App.config key names), `AppSettings` (typed reads), `EditableSettings`
    (load, validate and save for the Settings dialog) and `ConfigFile` (edits `<appSettings>` in the .config XML
    in place, keeping comments). Settings are read at startup, so saved changes apply after a restart. A new
    user-editable key needs a property and validation in `EditableSettings` and a field in `SettingsWindow.xaml`.
    Exception: `FontSettings` (Settings → Font settings) applies at once through `DynamicResource FontSize*` resources
    in `Styles.xaml`; a new text style gets a key in `SettingKeys`, an item in `FontSettings` and a default resource.
- `DataSync` (WPF)
  - `App.xaml.cs`: startup sequence (logger → splash + `StartupChecks`, which retries an unreachable remote database
    until it answers or the user clicks Exit → auto-login from `PW.txt` or `LoginWindow`
    → `MainWindow`) and `App.Restart()`.
  - `ViewModels\MainViewModel`: a `DispatcherTimer` polls `ReplicationEngine.GetStatus()` every second. Engine
    events come from background threads, go into a `ConcurrentQueue`, and are drained on the timer. Never touch UI
    state from engine threads.
  - `Infrastructure\`: `ObservableObject`, `RelayCommand`, `IShell` (dialogs and restart for view models).
  - `Themes\Styles.xaml`: all colours and control styles.
- `SQL\`: the setup scripts the app depends on. When the C# code needs a new or changed procedure, update them.

## Conventions

- Block-scoped namespaces, `using (...)` blocks, no nullable reference types (C# 7.3).
- Long-running or blocking work (database calls, `engine.Start/Stop/ResumeSync`) runs with `Task.Run`, not on the
  UI thread.
- Store classes don't catch exceptions. Replication tasks report them through `ReplicationTask.OnFailure`, and UI
  callers log and show them.
- New replication behaviour gets a test against `InMemoryProcessDataStore`.
