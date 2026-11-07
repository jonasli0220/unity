@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0InstallContextMenu.ps1" -AllUsers
echo.
pause
