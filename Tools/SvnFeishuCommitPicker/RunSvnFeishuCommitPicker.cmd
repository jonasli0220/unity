@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
if /I "%~1"=="-ListTickets" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%SvnFeishuCommitPicker.ps1" %*
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%SvnFeishuCommitPicker.ps1" "%~1" "%~2" "%~3" %4 %5 %6 %7 %8 %9
)
exit /b %ERRORLEVEL%
