@echo off
rem Build DataSync (Release). Closes on success, pauses on failure.
cd /d "C:\. Projects\DataSync2"
dotnet build DataSync.sln -c Release --nologo
if errorlevel 1 (
  echo.
  echo BUILD FAILED.
  pause
) else (
  exit
)

