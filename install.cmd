@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if errorlevel 1 (
  echo.
  echo Claude Launcher installation failed.
  pause
  exit /b 1
)
echo.
pause
