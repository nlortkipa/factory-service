@echo off
rem Compatibility entry point for older startup registrations. Never kill other sessions.
start "" "%~dp0FactoryService.Companion.exe" --background --installed
