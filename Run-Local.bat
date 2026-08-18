@echo off
setlocal EnableExtensions
pushd "%~dp0"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-FactoryService.ps1"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
  echo.
  echo Local startup failed with exit code %EXIT_CODE%.
  pause
)

popd
exit /b %EXIT_CODE%
