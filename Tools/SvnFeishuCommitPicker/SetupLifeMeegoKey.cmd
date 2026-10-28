@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0SetupLifeMeegoKey.ps1"
exit /b %ERRORLEVEL%
