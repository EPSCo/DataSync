@echo off
rem Build DataSync (Debug) and run in UI design mode (--design).
cd /d "C:\. Projects\DataSync2"
dotnet build DataSync.sln -c Debug --nologo
if errorlevel 1 (
  echo.
  echo BUILD FAILED.
  pause
  exit /b 1
)
start "" "C:\. Projects\DataSync2\DataSync\bin\Debug\net48\DataSync.exe" --design
exit

