# Factory Service Windows Agent

This is a .NET 10 Windows agent split into two processes:

- **FactoryService.Service** runs as LocalSystem, starts at boot, and launches a
  companion with each logged-in user's Windows token and environment. It handles
  users already signed in during installation, future logins, reconnects, and
  retries missing companions every ten seconds.
- **FactoryService.Companion** runs in the signed-in user's interactive session.
  It finds the Turmo USB panel's serial port automatically and sends a 320×480 QR design when it
  starts, after a successful rating (checked every five seconds), at QR expiry, and when the session becomes active or is
  unlocked. It also provides a tray icon. The desktop and Start menu shortcuts
  open its window for on-demand QR refresh.

Only the **unlocked user at the physical Windows console** may publish a QR to
the shared USB panel. Companions left running by Fast User Switching stay idle;
remote sessions do not take over the physical panel. The process must also run
as the console user's Windows identity (an app launched with another user's
administrator credentials cannot publish that administrator's QR).

On a user switch or unlock, the foreground companion first replaces the previous
QR with an unavailable screen, then requests that user's QR. A missing, blank,
or malformed configuration keeps the unavailable screen visible. Background
refreshes and delayed API responses cannot replace or clear the foreground
user's QR. All companions share a Windows mutex to serialize complete panel
frames, with ownership checked again immediately before transmission. Session
changes are detected through Windows notifications and a one-second fallback poll.

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

Installed startup uses `--installed`, which always reads that user's local file
and ignores shared environment overrides for the config path, API URL, and
operator ID. Direct development launches retain the environment overrides.

The companion reloads `config.json` before every QR request. Changes therefore
take effect on the next five-second check, a tray-menu refresh, or immediately
after locking and unlocking Windows (`Win+L`). A new Windows login also starts
the companion with the latest configuration; restarting the whole PC is not
required.

Open the client and select **QR კოდების გამორთვა** to remove the operator
assignment for the current Windows user. The shared client and other users'
assignments stay in place. Run an operator-specific installer to assign an
operator again.

Configuration read errors clear the QR; the companion never falls back to a
previously loaded operator. Requests in progress are cancelled when session
ownership changes, and responses for a configuration that has changed are discarded.

Run the Windows multi-user regression suite (no panel or API server required):

```powershell
dotnet run --project .\tests\FactoryService.Companion.Tests --configuration Release
```

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
Run the installer **once per computer**, from any account with administrator
approval. It installs into Program Files and starts the service for all users.
The service launches companions for all users already signed in, including
disconnected Fast User Switching sessions. Future users get their own companion
when they log in. Each process runs as that session's user, even when Test runs
the installer and supplies Administrator credentials for elevation.

The machine-wide Windows Run key is a login fallback. Duplicate automatic starts
exit silently. Both startup paths use the installed executable in Program Files;
neither depends on the original extracted package or the installer's Desktop.
The elevated installer never launches a companion with its own token.

If another account cannot read the package on Administrator's Desktop, extract
the ZIP in a shared folder such as `C:\Users\Public\Documents\Factory Service Client`.
Do not change permissions on Administrator's profile. Once installed, users do
not need access to the installer at all.

To update an existing installation, extract the new ZIP and run its `Install.cmd`.
This stops the service before the old companions, replaces the binaries, and
starts the service again. It then launches companions for all existing sessions
within ten seconds. Each user's config stays in place; there is no need to
reinstall, reboot, or sign out separately for another user.

For a physical-panel check, configure Administrator, leave Test's operator ID
blank, and switch between them without signing out. Administrator should show
its own QR; Test should show the unavailable screen. Wait over one minute in
each account to confirm there is no toggling, then configure Test with a different
operator and repeat. Also check lock/unlock and switching while the API is slow
or unavailable.

Double-click `Uninstall.cmd` from the extracted package to remove the service
and companion startup entry. The uninstaller deliberately leaves installed files and each user's
configuration in place so they can be recovered or inspected.
