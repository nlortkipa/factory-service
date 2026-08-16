# Factory Service Windows Agent

This is a .NET 10 Windows agent split into two processes:

- **FactoryService.Service** runs as a Windows Service, starts at boot, owns global
  machine configuration, records Windows session changes, and is the future home
  of ESP32/API communication.
- **FactoryService.Companion** runs in the signed-in user's interactive session.
  It sends a 320×480 QR design directly to the Turmo USB panel on COM3 when it
  starts, every minute, and whenever Windows reports that the session was
  unlocked. It also provides a tray icon and does not open a desktop window.

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

The companion immediately sends the configured QR to the Turmo panel. Double-click
the tray icon to refresh the QR manually. Close the official TURMO app first,
because only one process can use COM3 at a time.

## Configuration

Configuration is source/deployment-local: `windows-agent/config.json` is copied
beside both executables and is never stored under AppData or ProgramData.
`config.example.json` documents the only two fields: `apiBaseUrl` and
`operatorId`. Create the operator in the administration portal, then copy its
generated Operator ID into this file. The companion requests short-lived,
backend-signed QR sessions for that operator and never contains the backend
signing key.

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
