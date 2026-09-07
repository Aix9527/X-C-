@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%start-self-hosted-runner.ps1"
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
  echo.
  echo Runner startup did not complete. Exit code: %EXIT_CODE%
  echo Check the message above and verify that the GitHub Actions Runner is downloaded and registered.
  pause
)
exit /b %EXIT_CODE%
