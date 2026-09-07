@echo off
setlocal
chcp 65001 >nul
set "SCRIPT_DIR=%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%start-self-hosted-runner.ps1"
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
  echo.
  echo 启动未完成，错误码：%EXIT_CODE%
  echo 请根据上方提示检查 Runner 是否已下载并注册。
  pause
)
exit /b %EXIT_CODE%
