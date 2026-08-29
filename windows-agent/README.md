# Factory Service Windows Agent

This is a .NET 10 Windows agent split into two processes:

- **FactoryService.Service** runs as a Windows Service, starts at boot, records
  Windows session changes, and is the future home of ESP32/API communication.
- **FactoryService.Companion** runs in the signed-in user's interactive session.
  It finds the Turmo USB panel's serial port automatically and sends a 320×480 QR design when it
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
because only one process can use the panel's serial port at a time.

## Configuration

Configuration is stored separately for each Windows user:

```text
%LOCALAPPDATA%\Factory Service Agent\config.json
```

On a user's first companion launch, the application creates this file directly
with `apiBaseUrl` set to `http://localhost:5000` and a blank `operatorId`. There
is no shared or default configuration in the installed application. Later
changes to one user's `config.json` do not affect any other user.
`config.example.json` documents the only two fields. Set the production API URL,
create the operator in the administration portal, then copy its generated
Operator ID into the appropriate user's file. The companion requests
short-lived, backend-signed QR sessions for that operator and never contains the
backend signing key.

The companion reloads `config.json` before every QR request. Changes therefore
take effect on the next one-minute refresh, a tray-menu refresh, or immediately
after locking and unlocking Windows (`Win+L`). A new Windows login also starts
the companion with the latest configuration; restarting the whole PC is not
required.

Logs are written to:

```text
%PROGRAMDATA%\Factory Service Agent\logs\service.log
%LOCALAPPDATA%\Factory Service Agent\logs\companion.log
```

## Package and install

Build the self-contained Windows x64 package from the repository root:

```powershell
.\Create-Production-Client-Zip.bat
```

Extract `dist\FactoryServiceClient-win-x64.zip` and double-click `Install.cmd`.
The packaged installer installs the service with automatic
startup, registers the companion for every user's logon through the machine-wide
Windows Run key, and launches the companion for the current session. Each signed-in
user therefore receives a separate companion in that user's interactive session.
Because the companion stays running, it can show the QR again after every unlock.

Double-click `Uninstall.cmd` from the extracted package to remove the service
and companion startup entry. The uninstaller deliberately leaves installed files and each user's
configuration in place so they can be recovered or inspected.
