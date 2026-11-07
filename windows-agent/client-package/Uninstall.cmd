@echo off
setlocal EnableExtensions

fltmc >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator permission...
  powershell.exe -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

reg.exe delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Run" /v FactoryServiceCompanion /f >nul 2>&1
sc.exe stop FactoryServiceAgent >nul 2>&1
powershell.exe -NoProfile -Command "$s = Get-Service FactoryServiceAgent -ErrorAction SilentlyContinue; if ($s) { $s.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }"
if errorlevel 1 exit /b 1
taskkill.exe /IM FactoryService.Companion.exe /F >nul 2>&1
sc.exe delete FactoryServiceAgent >nul 2>&1
schtasks.exe /Change /TN "TURMO_ScheduleTask" /Enable >nul 2>&1

echo.
echo Factory Service Client startup was removed.
echo You may delete "%ProgramFiles%\Factory Service Agent" after restarting Windows.
pause
exit /b 0
