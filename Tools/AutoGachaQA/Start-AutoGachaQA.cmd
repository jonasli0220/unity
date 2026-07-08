@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0AutoGachaQA.ps1"
if errorlevel 1 (
  echo.
  echo 工具启动失败，请把本窗口中的报错截图发给维护者。
  pause
)
endlocal
