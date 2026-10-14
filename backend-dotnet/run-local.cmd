@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-local.ps1"
if errorlevel 1 (
  echo.
  echo .NET startup failed. Please copy the displayed error output.
  pause
)
