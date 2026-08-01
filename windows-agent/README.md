# Factory Service Windows Agent

This is a .NET 10 Windows agent split into two processes:

- **FactoryService.Service** runs as a Windows Service, starts at boot, owns global
  machine configuration, records Windows session changes, and is the future home
  of ESP32/API communication.
- **FactoryService.Companion** runs in the signed-in user's interactive session.
  It shows a full-screen QR when it starts and whenever Windows reports that the
  session was unlocked. It also provides a tray icon.

Windows isolates services in Session 0, so the service intentionally does not
attempt to draw the QR on the user's desktop.

## Run during development

Build everything:

```powershell
dotnet build .\FactoryService.sln
```

Run the service in console/debug mode:

```powershell
dotnet run --project .\src\FactoryService.Service
```

Run the interactive companion:

```powershell
dotnet run --project .\src\FactoryService.Companion
```

The companion immediately displays the configured QR. Press `Esc` to close the
QR window; the tray process remains running. Double-click the tray icon to show
the QR again. Lock and unlock Windows to test the session notification.

## Configuration

Configuration is source/deployment-local: `windows-agent/config.json` is copied
beside both executables and is never stored under AppData or ProgramData.
`config.example.json` documents the fields. The companion requests short-lived,
backend-signed QR sessions and never contains the backend signing key.

Logs are written to:

```text
<executable directory>\logs\service.log
<executable directory>\logs\companion.log
```

## Install

From an elevated PowerShell window:

```powershell
.\scripts\install.ps1
```

The installer publishes both executables as self-contained Windows applications,
installs the service with automatic
startup, registers the companion for every user's logon through the machine-wide
Windows Run key, and launches the companion for the current session. Each signed-in
user therefore receives a separate companion in that user's interactive session.
Because the companion stays running, it can show the QR again after every unlock.

To remove the service and companion startup entry:

```powershell
.\scripts\uninstall.ps1
```

The uninstall script deliberately leaves installed files and configuration in
place so they can be recovered or inspected.
