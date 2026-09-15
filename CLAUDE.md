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
    holds the pure range arithmetic. `ReplicationTask` provides the background loop, 1–60 s back-off and throttling.
  - `Data\`: `DatabaseConfig` (reads `.eps` files, hardware lock, connection checks), `SqlDb` (small ADO.NET
    stored-procedure mapper), `SqlProcessDataStore` (`IProcessDataStore` over SQL Server; `SaveBatch` uses
    `SqlBulkCopy` into a temp table plus `INSERT … WHERE NOT EXISTS`), `UserRepository`.
  - `Security\`: DES `Encryption` (must stay byte-compatible with DDUtility), `CredentialStore` (`PW.txt`),
    `ClearDataPassword` (date-based).
  - `Logging\Log`: static rolling file logger configured from App.config; never throws.
- `DataSync` (WPF)
  - `App.xaml.cs`: startup sequence (logger → splash + `StartupChecks` → auto-login from `PW.txt` or `LoginWindow`
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
