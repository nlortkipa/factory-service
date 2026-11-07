@echo off
setlocal EnableExtensions
fltmc >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator permission...
  set "FACTORY_SERVICE_INSTALLER=%~f0"
  powershell.exe -NoProfile -Command "Start-Process -FilePath $env:FACTORY_SERVICE_INSTALLER -Verb RunAs"
  exit /b
)

set "INSTALL_DIR=%ProgramFiles%\Factory Service Agent"
set "PAYLOAD_DIR=%~dp0payload"
set "SERVICE_EXE=%INSTALL_DIR%\FactoryService.Service.exe"
set "COMPANION_EXE=%INSTALL_DIR%\FactoryService.Companion.exe"
if not exist "%PAYLOAD_DIR%\FactoryService.Service.exe" goto incomplete
if not exist "%PAYLOAD_DIR%\FactoryService.Companion.exe" goto incomplete

rem Stop supervision before stopping companions, so an update cannot race a restart.
sc.exe query FactoryServiceAgent >nul 2>&1
if not errorlevel 1 (
  sc.exe stop FactoryServiceAgent >nul 2>&1
  powershell.exe -NoProfile -Command "$s = Get-Service FactoryServiceAgent -ErrorAction Stop; $s.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))"
  if errorlevel 1 goto failed
)
schtasks.exe /Change /TN "TURMO_ScheduleTask" /Disable >nul 2>&1
taskkill.exe /IM TURMO.exe /F >nul 2>&1
taskkill.exe /IM FactoryService.Companion.exe /F >nul 2>&1

if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
xcopy.exe "%PAYLOAD_DIR%\*" "%INSTALL_DIR%\" /E /I /Y /Q >nul
if errorlevel 1 goto failed

sc.exe query FactoryServiceAgent >nul 2>&1
if errorlevel 1 (
  sc.exe create FactoryServiceAgent binPath= "\"%SERVICE_EXE%\"" DisplayName= "Factory Service Agent" start= auto >nul
) else (
  sc.exe config FactoryServiceAgent binPath= "\"%SERVICE_EXE%\"" start= auto obj= LocalSystem >nul
)
if errorlevel 1 goto failed
sc.exe description FactoryServiceAgent "Starts a Factory Service companion for every logged-in Windows user." >nul
sc.exe failure FactoryServiceAgent reset= 86400 actions= restart/5000/restart/15000/restart/30000 >nul
reg.exe add "HKLM\Software\Microsoft\Windows\CurrentVersion\Run" /v FactoryServiceCompanion /t REG_SZ /d "\"%COMPANION_EXE%\" --background --installed" /f >nul
if errorlevel 1 goto failed

rem The service launches companions using each user's own token and environment.
rem Never launch a companion with the elevated installer's Administrator token.
sc.exe start FactoryServiceAgent >nul
if errorlevel 1 goto failed

echo.
echo Factory Service Client installed for ALL Windows users.
echo Already signed-in users start within 10 seconds. Future users start at login.
echo Each user loads their own config. No reinstall or sign-out is needed per user.
if /I not "%~1"=="--unattended" pause
exit /b 0

:incomplete
echo This package is incomplete. Extract the entire ZIP before running Install.cmd.
if /I not "%~1"=="--unattended" pause
exit /b 1

:failed
echo Installation failed with error code %errorlevel%.
if /I not "%~1"=="--unattended" pause
exit /b 1
