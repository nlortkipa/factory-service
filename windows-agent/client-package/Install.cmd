@echo off
setlocal EnableExtensions

fltmc >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator permission...
  powershell.exe -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

set "INSTALL_DIR=%ProgramFiles%\Factory Service Agent"
set "PAYLOAD_DIR=%~dp0payload"
set "SERVICE_EXE=%INSTALL_DIR%\FactoryService.Service.exe"
set "COMPANION_EXE=%INSTALL_DIR%\FactoryService.Companion.exe"

if not exist "%PAYLOAD_DIR%\FactoryService.Service.exe" goto incomplete
if not exist "%PAYLOAD_DIR%\FactoryService.Companion.exe" goto incomplete

rem TURMO.exe and the factory companion cannot own the panel's serial port at the same time.
rem Disable but do not delete the vendor's login task.
schtasks.exe /Change /TN "TURMO_ScheduleTask" /Disable >nul 2>&1
taskkill.exe /IM TURMO.exe /F >nul 2>&1
taskkill.exe /IM FactoryService.Companion.exe /F >nul 2>&1

sc.exe query FactoryServiceAgent >nul 2>&1
if not errorlevel 1 (
  sc.exe stop FactoryServiceAgent >nul 2>&1
  timeout.exe /T 2 /NOBREAK >nul
)

if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
xcopy.exe "%PAYLOAD_DIR%\*" "%INSTALL_DIR%\" /E /I /Y /Q >nul
if errorlevel 1 goto failed

sc.exe query FactoryServiceAgent >nul 2>&1
if errorlevel 1 (
  sc.exe create FactoryServiceAgent binPath= "\"%SERVICE_EXE%\"" DisplayName= "Factory Service Agent" start= auto >nul
) else (
  sc.exe config FactoryServiceAgent binPath= "\"%SERVICE_EXE%\"" start= auto >nul
)
if errorlevel 1 goto failed

sc.exe description FactoryServiceAgent "Kartuli Cement factory service and session monitor." >nul
sc.exe config FactoryServiceAgent start= delayed-auto >nul
sc.exe failure FactoryServiceAgent reset= 86400 actions= restart/5000/restart/15000/restart/30000 >nul
reg.exe add "HKLM\Software\Microsoft\Windows\CurrentVersion\Run" /v FactoryServiceCompanion /t REG_SZ /d "\"%COMPANION_EXE%\"" /f >nul
if errorlevel 1 goto failed

sc.exe start FactoryServiceAgent >nul
start "" "%COMPANION_EXE%"

echo.
echo Factory Service Client installed successfully.
echo Server: https://factory-service-app.vercel.app
echo It will start automatically with Windows and user login.
pause
exit /b 0

:incomplete
echo.
echo This package is incomplete. Extract the entire ZIP before running Install.cmd.
pause
exit /b 1

:failed
echo.
echo Installation failed with error code %errorlevel%.
pause
exit /b 1
