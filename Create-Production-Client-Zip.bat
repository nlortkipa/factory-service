@echo off
setlocal EnableExtensions
pushd "%~dp0"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows-agent\scripts\package-client.ps1" -Configuration Release -Runtime win-x64
set "EXIT_CODE=%ERRORLEVEL%"

if "%EXIT_CODE%"=="0" (
  echo.
  echo Production client ZIP created in:
  echo %~dp0windows-agent\dist
) else (
  echo.
  echo Production client build failed with exit code %EXIT_CODE%.
  pause
)

popd
exit /b %EXIT_CODE%
