@echo off
setlocal
title SVN Feishu ticket service - keep open
set "SVN_FEISHU_SERVER_DIR=%~dp0"

if "%MEEGO_BASE_API_KEY%"=="" (
  for /f "usebackq tokens=2,*" %%A in (`reg query HKCU\Environment /v MEEGO_BASE_API_KEY 2^>nul`) do set "MEEGO_BASE_API_KEY=%%B"
)

if "%MEEGO_BASE_API_KEY%"=="" (
  echo Missing MEEGO_BASE_API_KEY.
  echo.
  echo Run this first:
  echo ..\SetupLocalMeegoKey.cmd
  echo.
  exit /b 1
)

echo SVN Feishu ticket service is running.
echo Endpoint: http://127.0.0.1:18765/api/my-open-workitems
echo Keep this window open while using TortoiseSVN.
echo You can minimize it. Close it only when you no longer need SVN ticket lookup.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ticket-service.ps1" -ConfigPath "%~dp0server.config.meego-base.sample.json"
