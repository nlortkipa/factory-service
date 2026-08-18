@echo off
timeout /t 8 /nobreak >nul
taskkill /im TURMO.exe /f >nul 2>&1
taskkill /im FactoryService.Companion.exe /f >nul 2>&1
start "" "%~dp0FactoryService.Companion.exe"
