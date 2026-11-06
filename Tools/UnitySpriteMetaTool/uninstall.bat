@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0UninstallContextMenu.ps1"
echo.
pause
