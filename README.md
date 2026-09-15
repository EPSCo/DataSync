# DataSync

DataSync copies drilling data (`TProcessData`) from a remote DDR/DDM SQL Server database into a local SQL Server
database (normally `OfficeDDR`) and keeps the copy up to date. It is a WPF rewrite of **DDRREP**. It uses the same
databases, stored procedures, `.eps` connection files and `PW.txt`, so it can replace DDRREP on a machine that
already runs it.

## Dependencies

None beyond the .NET Framework 4.8. The app and `DataSync.Core` use no NuGet packages and no third-party DLLs:

| DDRREP used | DataSync uses |
|---|---|
| Telerik RadControls WinForms | Standard WPF controls and styles (`DataSync/Themes/Styles.xaml`) |
| Serilog + Serilog.Sinks.File | Built-in rolling file logger (`DataSync.Core/Logging/Log.cs`) |
| Copied-in Dapper (`SqlMapper.cs`) | Plain ADO.NET (`DataSync.Core/Data/SqlDb.cs`) |

Only the test project uses NuGet (MSTest), and it is not deployed.

## Solution layout

| Project | What it holds |
|---|---|
| `DataSync.Core` | Replication engine, SQL data access, `.eps`/hardware lock, credentials, logging. No UI. |
| `DataSync` | WPF app (MVVM without a framework): splash, login, main window, clear-data dialog. |
| `DataSync.Tests` | MSTest tests for the replication engine against an in-memory store (no database needed). |
| `SQL\` | Database setup scripts (the same as DDRREP's). |
| `Docs\` | User manual. |

## Build

Requirements: Visual Studio 2022 (or .NET SDK 6+) and the .NET Framework 4.8 targeting pack
(`C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8`).

```bash
dotnet build DataSync.sln -c Release
```

```bash
dotnet test DataSync.Tests\DataSync.Tests.csproj
```

## UI design mode

Debug builds started with `--design` skip the hardware lock, database checks, login and replication, and show sample
data. You can work on the UI without `.eps` files or databases:

```bash
DataSync\bin\Debug\net48\DataSync.exe --design
```

Release builds ignore the flag.

## Deploy

1. Copy `DataSync\bin\Release\net48\` to the target machine. The target needs the .NET Framework 4.8 runtime.
2. Put `local.eps` and `remote.eps` (created with DDUtility for that machine) next to `DataSync.exe`.
3. Adjust `DataSync.exe.config` (rig name, replication tuning, logging).

Set up the databases as described in the [user manual](Docs/DataSync_UserManual.md).
