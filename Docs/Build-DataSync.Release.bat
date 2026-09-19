@echo off
rem Build DataSync (Release).
cd /d "C:\. Projects\DataSync2"
dotnet build DataSync.sln -c Release --nologo
if errorlevel 1 (
  echo.
  echo BUILD FAILED.
) else (
  echo.
  echo BUILD SUCCEEDED.
)
pause

